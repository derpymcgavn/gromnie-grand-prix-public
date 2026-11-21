using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const string CrossingArt = "res://Data/Olthoi-Crossing.png";
    private readonly StandardMaterial3D encounterPost = BogCircuit.Paint("4a392e");
    private readonly StandardMaterial3D encounterPlate = BogCircuit.Paint("eebd46");
    private readonly StandardMaterial3D lampOff = BogCircuit.Paint("745626");
    private readonly StandardMaterial3D lampOn = Glow(new("ffb32d"));
    private readonly StandardMaterial3D barrelWood = BogCircuit.Paint("83532e");
    private readonly StandardMaterial3D barrelHead = BogCircuit.Paint("ad7741");
    private readonly StandardMaterial3D barrelIron = BogCircuit.Paint("434b4e");
    private readonly CylinderMesh barrelHoop = new() { TopRadius = .49f, BottomRadius = .49f, Height = .09f, RadialSegments = 20 };
    private readonly CylinderMesh barrelLid = new() { TopRadius = .43f, BottomRadius = .43f, Height = .07f, RadialSegments = 20 };
    private readonly SphereMesh encounterLamp = new() { Radius = .18f, Height = .35f, RadialSegments = 12, Rings = 6 };
    private ArrayMesh? barrelStaves;
    private Texture2D? crossingArt;

    private void BuildEncounterSigns(RaceEncounter site)
    {
        bool swarm = site.Kind == EncounterKind.OlthoiCrossing;
        foreach (int side in new[] { -1, 1 })
        {
            bool mainCrossingSign = site.Kind != EncounterKind.TuskerToss && side > 0;
            float at = site.Distance - (mainCrossingSign ? 5 : 19);
            var sign = new Node3D
            {
                Name = site.Warning,
                Position = EncounterGround(at, side * (track.WidthAt(at) + (mainCrossingSign ? 1.65f : 1.55f))),
                Rotation = new Vector3(0, BogCircuit.Yaw(track.Forward(at)) - side * .20f, 0),
                Scale = Vector3.One * (mainCrossingSign ? 1.18f : 1)
            };
            roadsideRoot.AddChild(sign);
            float postHeight = swarm ? 4.73f : 3.25f;
            BogCircuit.Box(sign, new(0, postHeight * .50f, -.09f), new(.20f, postHeight, .20f), encounterPost);
            if (swarm && (crossingArt ??= GD.Load<Texture2D>(CrossingArt)) is { } art)
            {
                sign.AddChild(new Sprite3D
                {
                    Name = "Olthoi crossing artwork",
                    Texture = art,
                    PixelSize = 3.55f / art.GetWidth(),
                    Position = new(0, 2.80f, .08f),
                    AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                    TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
                    Shaded = false
                });
            }
            float plaqueY = swarm ? .79f : 2.68f;
            BogCircuit.Box(sign, new(0, plaqueY, 0), new(3.65f, .69f, .11f), encounterPost);
            BogCircuit.Box(sign, new(0, plaqueY, .061f), new(3.48f, .57f, .045f), encounterPlate);
            sign.AddChild(new Label3D
            {
                Text = site.Warning,
                Position = new(0, plaqueY, .094f),
                FontSize = 65, PixelSize = .005f, OutlineSize = 0,
                Modulate = new Color("221809"),
                Shaded = false
            });
            site.Lamps.Add(BogCircuit.MeshAt(sign, encounterLamp, new(0, swarm ? 4.78f : 3.25f, .04f), Vector3.One, lampOff));
        }
    }

    private void SetEncounterLights(RaceEncounter site, bool lit)
    {
        var material = lit ? lampOn : lampOff;
        foreach (var lamp in site.Lamps)
            if (lamp.MaterialOverride != material) lamp.MaterialOverride = material;
    }

    private void StageEncounter(RaceEncounter site)
    {
        var stage = new Node3D { Name = site.Kind.ToString() };
        roadsideRoot.AddChild(stage);
        site.Stage = stage;
        site.LastThrow = site.LastCatch = -1;
        if (site.Kind == EncounterKind.RollingDeath)
        {
            StageRollingDeath(site);
            return;
        }
        if (site.Kind == EncounterKind.LugianQuarry)
        {
            StageLugianQuarry(site);
            return;
        }
        bool swarm = site.Kind == EncounterKind.OlthoiCrossing;
        var creature = AcCatalog.Enemies[swarm ? 2 : 1];
        for (int i = 0; i < (swarm ? site.Bodies.Length : 2); i++)
        {
            float scale = swarm ? creature.Scale * (.79f + (i % 4) * .07f) : 1.26f;
            var actor = assets.CreateActor(creature.Setup, creature.Motion, scale);
            stage.AddChild(actor);
            site.Cast.Add(actor);
            actor.OnHook = (hook, _) => raceAudio?.PlayEmote(hook, actor);
            if (swarm) continue;
            int side = i == 0 ? site.Side : -site.Side;
            actor.Position = EncounterGround(site.Distance + (i == 0 ? -2.25f : 2.25f), side * site.Reach);
            actor.Face(-side * track.Right(site.Distance));
            for (int cask = 0; cask < 2; cask++)
            {
                Vector3 at = EncounterGround(site.Distance + (cask == 0 ? -3.8f : 3.8f), side * (site.Reach + .15f));
                Node3D prop = BuildBarrel(stage);
                prop.Position = at + Vector3.Up * .54f;
                prop.Rotation = new Vector3(.03f, cask * .85f, -.08f);
            }
        }
        if (!swarm) site.Projectile = BuildBarrel(stage);
    }

    private void ReleaseEncounterStage(RaceEncounter site)
    {
        roadsideRoot.RemoveChild(site.Stage!);
        site.Stage!.QueueFree();
        site.Stage = site.Projectile = null;
        site.Cast.Clear();
        site.QuarryPile.Clear();
    }

    private void DrawTuskerToss(RaceEncounter site, float age, float dt)
    {
        float motion = age - EncounterWarningTime;
        int windup = Mathf.Clamp(Mathf.FloorToInt((motion + .40f) / TossPeriod), 0, 2);
        if (motion > -.40f && windup > site.LastThrow)
        {
            site.LastThrow = windup;
            site.Cast[windup % 2].Attack();
        }
        int catching = Mathf.FloorToInt((motion - TossTravel + .22f) / TossPeriod);
        if (catching is >= 0 and <= 2 && catching > site.LastCatch)
        {
            site.LastCatch = catching;
            site.Cast[(catching + 1) % 2].Attack();
        }
        foreach (var tusker in site.Cast) tusker.Tick(dt, false, Vector3.Zero);
        site.Projectile!.Position = site.Bodies[0].Position;
        float flight = Mathf.Clamp((motion - Math.Clamp(Mathf.FloorToInt(motion / TossPeriod), 0, 2) * TossPeriod) / TossTravel, 0, 1);
        site.Projectile.Rotation = new Vector3(.25f + flight * Mathf.Tau * 2, .35f, flight * Mathf.Tau);
    }

    private Node3D BuildBarrel(Node3D parent)
    {
        var barrel = new Node3D { Name = "Oak barrel" };
        parent.AddChild(barrel);
        barrelStaves ??= BuildBarrelStaves();
        BogCircuit.MeshAt(barrel, barrelStaves, Vector3.Zero, Vector3.One, barrelWood);
        foreach (float side in new[] { -1f, 1f })
        {
            BogCircuit.MeshAt(barrel, barrelHoop, Vector3.Up * (.31f * side), Vector3.One, barrelIron);
            BogCircuit.MeshAt(barrel, barrelLid, Vector3.Up * (.50f * side), Vector3.One, barrelHead);
        }
        return barrel;
    }

    private static ArrayMesh BuildBarrelStaves()
    {
        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        float[] levels = [-.54f, -.30f, 0, .30f, .54f];
        float[] radii = [.43f, .48f, .52f, .48f, .43f];
        const int staves = 18;
        for (int stave = 0; stave < staves; stave++)
        {
            float a = Mathf.Tau * (stave + .036f) / staves;
            float b = Mathf.Tau * (stave + .964f) / staves;
            for (int level = 0; level < levels.Length - 1; level++)
            {
                Vector3 left = Edge(a, level), right = Edge(b, level);
                Vector3 upperLeft = Edge(a, level + 1), upperRight = Edge(b, level + 1);
                surface.AddVertex(left);
                surface.AddVertex(upperRight);
                surface.AddVertex(upperLeft);
                surface.AddVertex(left);
                surface.AddVertex(right);
                surface.AddVertex(upperRight);
            }
        }
        surface.GenerateNormals();
        return surface.Commit();

        Vector3 Edge(float angle, int level) => new(Mathf.Cos(angle) * radii[level], levels[level], Mathf.Sin(angle) * radii[level]);
    }
}
