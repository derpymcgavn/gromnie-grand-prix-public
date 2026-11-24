using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const float JesterGalleryFraction = .067f;
    private const uint JesterIdle = 0x41000003, JesterLaugh = 0x13000080, JesterHeartyLaugh = 0x13000089,
        JesterBow = 0x1300007d, JesterPoint = 0x13000084, JesterPointDown = 0x1300014e,
        JesterMock = 0x130000cb, JesterDance = 0x13000151;
    private const uint JesterFireSetup = 0x020017df, JesterFireMotion = 0x090001ea,
        JesterMarkerSetup = 0x020017f7, JesterMarkerMotion = 0x090001ec;

    // The Graveyard quest and combat weenies share this native outfit and motion table.
    private static readonly RareCatalog.Creature GraveyardJesterLook = new()
    {
        Id = 72262, Name = "Jester", Setup = 0x020017d6, Motion = 0x090001e6,
        Clothing = 0x10000729, Palette = 0x0400007e, PaletteTemplate = 11, Scale = 1.3f
    };

    private Node3D? jesterCastRoot;
    private readonly List<JesterCastStage> jesterCastStages = [];

    private sealed class JesterCastStage(Node3D root, Node3D pivot, AcActor jester, AcActor[] fixtures, AcActor? eye,
        OmniLight3D light, uint[] emotes, float radius, float lift, float bank, float flare)
    {
        public readonly Node3D Root = root;
        public readonly Node3D Pivot = pivot;
        public readonly AcActor Jester = jester;
        public readonly AcActor[] Fixtures = fixtures;
        public readonly AcActor? Eye = eye;
        public readonly OmniLight3D Light = light;
        public readonly uint[] Emotes = emotes;
        public readonly float Radius = radius, Lift = lift, Bank = bank, Flare = flare;
        public bool InRange;
        public int NextEmote;
        public float NextAction, LastElapsed;
    }

    private void BuildJesterCast(Node3D root, BogCircuit circuit)
    {
        ClearJesterCast();
        var cast = new Node3D { Name = "The Graveyard Jester" };
        root.AddChild(cast);
        jesterCastRoot = cast;
        var stone = BogCircuit.Paint("302534");

        Stage("The Jester welcomes you", JesterGalleryFraction, -1, 8, 2.7f, 0, 0, "ffc37f", 145,
            [JesterBow, JesterLaugh, JesterPoint]);
        Stage("The Jester takes the wrong side", .423f, 1, 19, 5.5f, 7, -1.26f, "a7eee0", 220,
            [JesterPointDown, JesterDance, JesterMock]);
        Stage("A giant Jester breaks the hour", .618f, -1, 39, 16, 0, -.10f, "ffafba", 355,
            [JesterHeartyLaugh, JesterMock, JesterPointDown]);
        Stage("The Jester follows to the last door", .810f, 1, 18, 7, 3.5f, .22f, "fc908f", 235,
            [JesterMock, JesterHeartyLaugh, JesterBow]);

        void Stage(string name, float fraction, int side, float offset, float scale, float lift, float bank,
            string lightColor, float radius, uint[] emotes)
        {
            int phase = jesterCastStages.Count;
            float station = fraction * circuit.Length;
            Vector3 heading = circuit.Forward(station) with { Y = 0 };
            heading = heading.Normalized();
            Vector3 outside = heading.Cross(Vector3.Up) * side;
            Vector3 position = circuit.Point(station) + outside * (circuit.WidthAt(station) + offset);
            Vector3 facing = (-heading * .58f - outside * .81f).Normalized();
            var stage = new Node3D { Name = name, Position = position, Basis = Basis.LookingAt(facing) };
            cast.AddChild(stage);

            var tint = new Color(lightColor);
            float plinthRadius = 2.9f + scale * .56f;
            var plinth = BogCircuit.MeshAt(stage, new CylinderMesh
            {
                TopRadius = plinthRadius, BottomRadius = plinthRadius * .76f, Height = 1.8f, RadialSegments = 12
            }, Vector3.Down * .9f, Vector3.One, stone);
            plinth.Name = "Graveyard stone plinth";
            plinth.Layers |= JesterReflectionSourceLayer;
            plinth.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var rim = BogCircuit.MeshAt(stage, new TorusMesh
            {
                InnerRadius = plinthRadius * .84f, OuterRadius = plinthRadius * .86f, Rings = 32, RingSegments = 4
            }, Vector3.Up * .045f, Vector3.One, new StandardMaterial3D
            {
                AlbedoColor = tint.Darkened(.41f), EmissionEnabled = true, Emission = tint, EmissionEnergyMultiplier = .24f,
                Roughness = .7f
            });
            rim.Layers |= JesterReflectionSourceLayer;
            rim.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

            var pivot = new Node3D { Position = Vector3.Up * lift, Rotation = new Vector3(0, 0, bank) };
            stage.AddChild(pivot);
            var jester = assets.CreateActor(GraveyardJesterLook.Setup, GraveyardJesterLook.Motion, scale);
            pivot.AddChild(jester);
            jester.Name = "Native Jester";
            jester.ApplyNativeAppearance(GraveyardJesterLook);
            jester.Position = Vector3.Up * (.07f - MeasureVoidActorBody(jester).Position.Y * scale);
            LightVoidActor(jester, JesterReflectionSourceLayer, tint, phase == 3 ? .68f : .48f);

            float fixtureScale = Math.Clamp(scale * .5f, 1.4f, 3.1f);
            var fixtures = new AcActor[2];
            for (int i = 0; i < fixtures.Length; i++)
            {
                int edge = i == 0 ? -1 : 1;
                float flank = edge * plinthRadius * .65f;
                AddProp(stage, "Dericost pillar", 0x0200031d, 0, new Vector3(flank, 0, .9f), fixtureScale, tint, .20f);
                fixtures[i] = AddProp(stage, "Jester Fire", JesterFireSetup, JesterFireMotion,
                    new Vector3(flank, 2.56f * fixtureScale, .9f), fixtureScale * 1.26f, tint, .70f);
            }

            AcActor? eye = null;
            if (phase == 2)
                eye = AddProp(stage, "Jester’s Malevolent Eye", 0x020017dd, 0, new Vector3(0, 18, -10), 11, tint, .75f);
            if (phase == 3)
            {
                AddProp(stage, "Jester’s Prison Door", 0x02001651, 0x0900016f, new Vector3(0, .12f, 6), 4.4f, tint, .24f);
                fixtures = [.. fixtures, AddProp(stage, "Jester’s Marker", JesterMarkerSetup, JesterMarkerMotion,
                    new Vector3(0, 7.5f, -3.5f), 5f, tint, .64f)];
            }

            float flare = 1.7f + scale * .09f;
            var light = new OmniLight3D
            {
                Position = new Vector3(0, lift + scale * 1.1f, -plinthRadius * .8f), LightColor = tint,
                LightEnergy = flare, OmniRange = 10 + scale * 2.1f, OmniAttenuation = 1.4f,
                LightCullMask = JesterReflectionSourceLayer
            };
            stage.AddChild(light);
            jesterCastStages.Add(new(stage, pivot, jester, fixtures, eye, light, emotes, radius, lift, bank, flare));
        }

        AcActor AddProp(Node3D stage, string name, uint setup, uint motion, Vector3 at, float scale, Color tint, float fill)
        {
            var prop = assets.CreateActor(setup, motion, scale);
            stage.AddChild(prop);
            prop.Name = name;
            prop.Position = at;
            LightVoidActor(prop, JesterReflectionSourceLayer, tint, fill);
            return prop;
        }
    }

    private void TickJesterCast(float dt)
    {
        if (jesterCastRoot is null || racers.Count == 0) return;
        Vector3 near = Player.CoursePosition(track);
        foreach (var stage in jesterCastStages)
        {
            float radius = stage.Radius + (stage.InRange ? 24 : 0);
            if (stage.Root.GlobalPosition.DistanceSquaredTo(near) > radius * radius)
            {
                if (stage.InRange || stage.Root.Visible) stage.Root.Hide();
                stage.InRange = false;
                continue;
            }

            if (!stage.InRange || elapsed < stage.LastElapsed)
            {
                stage.InRange = true;
                stage.Root.Show();
                stage.NextEmote = 0;
                stage.NextAction = elapsed + .18f;
                stage.Jester.Play(JesterIdle, false);
            }
            stage.LastElapsed = elapsed;
            if (!stage.Jester.ActionBusy && elapsed >= stage.NextAction)
            {
                uint emote = stage.Emotes[stage.NextEmote];
                stage.Jester.Play(emote, true);
                stage.NextEmote = (stage.NextEmote + 1) % stage.Emotes.Length;
                stage.NextAction = elapsed + 2.1f;
            }

            stage.Jester.TickCurrentMotion(dt);
            foreach (var fixture in stage.Fixtures) fixture.TickCurrentMotion(dt);
            if (stage.Bank != 0)
                stage.Pivot.Rotation = new Vector3(0, 0, stage.Bank + .10f * Mathf.Sin(elapsed * 1.45f));
            if (stage.Lift > 0)
                stage.Pivot.Position = Vector3.Up * (stage.Lift + .72f * Mathf.Sin(elapsed * 1.18f));
            if (stage.Eye is { } eye)
            {
                eye.Position = new Vector3(12 * Mathf.Sin(elapsed * .54f), 17 + 3 * Mathf.Cos(elapsed * .86f), -10);
                eye.Rotation = new Vector3(.14f * Mathf.Sin(elapsed), .55f * Mathf.Sin(elapsed * .54f), 0);
            }
            stage.Light.LightEnergy = stage.Flare * (1 + .13f * Mathf.Sin(elapsed * 2.8f));
        }
    }

    private void ClearJesterCast()
    {
        jesterCastStages.Clear();
        if (GodotObject.IsInstanceValid(jesterCastRoot) && !jesterCastRoot!.IsQueuedForDeletion())
            jesterCastRoot.QueueFree();
        jesterCastRoot = null;
    }
}
