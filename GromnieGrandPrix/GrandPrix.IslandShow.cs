using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private static readonly RareCatalog.Creature IslandAsheronLook = new()
    {
        Id = 20193, Setup = 0x020009c8, Motion = 0x09000001,
        Palette = 0x04000eb2, Clothing = 0x100003b2, PaletteTemplate = 61, Shade = .5f
    };
    private static readonly RareCatalog.Creature IslandStraw = new()
    {
        Id = 2442, Setup = 0x02000034, Motion = 0x0900008a,
        Palette = 0x040001b9, Clothing = 0x1000017a, PaletteTemplate = 46, Shade = 1
    };
    private IslandShow? islandShow;

    private sealed class IslandShow(Node3D root, AcActor mage, AcActor[] targets, Vector3 audience)
    {
        public readonly Node3D Root = root;
        public readonly AcActor Mage = mage;
        public readonly AcActor[] Targets = targets;
        public readonly Vector3 Audience = audience;
        public Random Random = new();
        public float Clock, NextAct, ReleaseAt, LaunchedAt, TargetFlinch;
        public bool Dancing, Casting;
        public int Spell, Target;
        public Vector3 ReleasePoint, Impact;
        public Node3D? Projectile;
    }

    private void BuildIslandShow()
    {
        islandShow = null;
        if (!track.IsWaterCourse) return;
        Vector3 anchor = IslandShowAnchor;
        Vector3 audience = track.Point(track.Project(anchor, 0).Distance);
        Vector3 shoreward = FlatDirection(audience, anchor).Normalized();
        Vector3 across = shoreward.Cross(Vector3.Up);
        var stage = new Node3D { Name = "Asheron's practice range", Position = anchor };
        courseItems.AddChild(stage);
        Texture2D? stone = RegattaStoneTexture(), plate = RegattaPlateTexture();
        var plinth = new CylinderMesh { TopRadius = 4.8f, BottomRadius = 5.1f, Height = .46f, RadialSegments = 10 };
        BogCircuit.MeshAt(stage, plinth, Vector3.Up * .07f, Vector3.One, RegattaFinish(stone, "a09b81", .8f));
        BogCircuit.MeshAt(stage, new TorusMesh { InnerRadius = 4.3f, OuterRadius = 4.50f, Rings = 36, RingSegments = 6 },
            Vector3.Up * .34f, Vector3.One, RegattaFinish(plate, "66d4bd", .34f, .85f));
        var mage = assets.CreateActor(IslandAsheronLook.Setup, IslandAsheronLook.Motion, 3.15f);
        mage.Name = "Asheron";
        mage.ApplyNativeAppearance(IslandAsheronLook);
        DressIslandAsheronInWhite(mage);
        mage.Position = Vector3.Up * .32f;
        stage.AddChild(mage);
        mage.Face(-shoreward);
        stage.AddChild(new Label3D
        {
            Text = "ASHERON'S PRACTICE RANGE", Position = Vector3.Up * 8.2f,
            FontSize = 57, PixelSize = .018f, OutlineSize = 8, Modulate = new Color("fff0c8"),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, VisibilityRangeEnd = 132, VisibilityRangeEndMargin = 20
        });

        var practice = assets.CreateActor(IslandStraw.Setup, IslandStraw.Motion);
        StandardMaterial3D straw;
        try
        {
            practice.ApplyNativeAppearance(IslandStraw);
            var source = practice.BodyMeshes.Where(mesh => mesh.Mesh is not null)
                .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh!.GetSurfaceCount())
                    .Select(surface => mesh.Mesh.SurfaceGetMaterial(surface)))
                .OfType<StandardMaterial3D>().FirstOrDefault(material => material.AlbedoTexture is not null);
            straw = new StandardMaterial3D
            {
                AlbedoTexture = source?.AlbedoTexture,
                AlbedoColor = source is null ? new Color("bd9252") : new Color("f6e2b6"),
                Roughness = .92f, CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
        }
        finally { practice.Free(); }

        AcActor[] targets = new AcActor[3];
        var olthoi = AcCatalog.Enemies[2];
        for (int i = 0; i < targets.Length; i++)
        {
            Vector3 at = anchor + shoreward * (7 + 2 * (i % 2)) + across * (i - 1) * 6.8f;
            Vector3 footing = track.FreeGround(at) - anchor;
            var target = assets.CreateActor(olthoi.Setup, 0, 1.65f);
            target.Name = $"Straw Olthoi {i + 1}";
            target.Position = footing + Vector3.Up * .28f;
            stage.AddChild(target);
            target.Face(FlatDirection(target.Position, mage.Position));
            foreach (var mesh in target.BodyMeshes) mesh.MaterialOverride = straw;
            var stand = new CylinderMesh { TopRadius = 1.22f, BottomRadius = 1.40f, Height = .36f, RadialSegments = 8 };
            BogCircuit.MeshAt(stage, stand, footing + Vector3.Up * .12f, Vector3.One, RegattaFinish(stone, "9a6439", .84f));
            targets[i] = target;
        }
        AddIslandReflectionLayers(stage);
        islandShow = new(stage, mage, targets, audience);
    }

    private static void DressIslandAsheronInWhite(AcActor mage)
    {
        Shader shader = GD.Load<Shader>("res://Shaders/IslandAsheronRobe.gdshader");
        var cloth = new Dictionary<Texture2D, ShaderMaterial>();
        // The native appearance uses dark cloth. Keep its folds and embroidery in the new dye.
        foreach (int part in new[] { 0, 1, 2, 3, 5, 6, 7, 9, 10, 11, 13, 14, 16 })
        foreach (var mesh in mage.Part(part).GetChildren().OfType<MeshInstance3D>())
        {
            if (mesh.Mesh is not { } geometry) continue;
            for (int surface = 0; surface < geometry.GetSurfaceCount(); surface++)
            {
                if (geometry.SurfaceGetMaterial(surface) is not StandardMaterial3D { AlbedoTexture: { } texture }) continue;
                if (!cloth.TryGetValue(texture, out var dyed))
                {
                    dyed = new ShaderMaterial { Shader = shader };
                    dyed.SetShaderParameter("cloth_texture", texture);
                    cloth.Add(texture, dyed);
                }
                mesh.SetSurfaceOverrideMaterial(surface, dyed);
            }
        }
    }

    private void ResetIslandShow()
    {
        if (islandShow is not { } show) return;
        show.Projectile?.QueueFree();
        show.Projectile = null;
        show.Clock = show.ReleaseAt = show.TargetFlinch = 0;
        show.NextAct = .4f;
        show.Dancing = show.Casting = false;
        show.Random = onlinePlan is { } plan ? new Random(plan.Seed ^ 3269) : testMode ? new Random(3269) : new Random();
        show.Mage.Play(0x41000003, false);
        foreach (var target in show.Targets) target.RotationDegrees = target.RotationDegrees with { Z = 0 };
    }

    private void BeginIslandShowAct(IslandShow show, bool cast)
    {
        var mage = show.Mage;
        show.Dancing = false;
        if (cast)
        {
            show.Spell = show.Random.Next(CitadelMagic.Length);
            show.Target = show.Random.Next(show.Targets.Length);
            mage.Face(FlatDirection(mage.Position, show.Targets[show.Target].Position));
            show.Casting = mage.Cast(false);
            if (show.Casting)
            {
                show.ReleaseAt = show.Clock + Math.Max(.18f, mage.AttackImpactDelay);
                show.NextAct = show.Clock + Math.Max(3.55f, mage.LastActionDuration + .8f);
                return;
            }
        }

        mage.Face(FlatDirection(mage.GlobalPosition, show.Audience));
        uint dance = show.Random.Next(3) == 0 ? YmcaEmote : DanceEmote;
        show.Dancing = mage.Play(dance, true) || mage.Play(YmcaEmote, true);
        show.NextAct = show.Clock + 3.2f + show.Random.NextSingle() * 2;
    }

    private void TickIslandShow(float dt)
    {
        if (islandShow is not { } show) return;
        bool near = show.Root.Position.DistanceSquaredTo(Player.CoursePosition(track)) < 155 * 155;
        if (!near && !show.Casting && show.Projectile is null) return;
        show.Clock += dt;
        if (show.Clock >= show.NextAct && !show.Mage.ActionBusy && !show.Casting && show.Projectile is null)
            BeginIslandShowAct(show, show.NextAct > .4f && show.Random.NextSingle() < .52f);
        show.Mage.TickCurrentMotion(dt);

        var target = show.Targets[show.Target];
        if (show.TargetFlinch > 0)
        {
            show.TargetFlinch = Math.Max(0, show.TargetFlinch - dt);
            float sway = Mathf.Sin(show.TargetFlinch * 18) * show.TargetFlinch * .35f;
            target.Rotation = target.Rotation with { Z = sway };
        }

        var (model, tint) = CitadelMagic[show.Spell];
        if (show.Casting && show.Clock >= show.ReleaseAt)
        {
            show.Casting = false;
            show.ReleasePoint = show.Mage.ToGlobal(show.Mage.BodyPartTransform(12).Origin);
            show.Impact = target.GlobalPosition + Vector3.Up * 1.7f;
            show.LaunchedAt = show.Clock;
            var bolt = new Node3D { Name = "Asheron's practice spell" };
            show.Root.AddChild(bolt);
            bolt.GlobalPosition = show.ReleasePoint;
            bolt.LookAt(show.Impact);
            bolt.AddChild(assets.CreateActor(model.Setup, 0, 1.05f));
            BogCircuit.MeshAt(bolt, citadelCore, Vector3.Zero, new(1.35f, 1.35f, 1.9f), RegattaFinish(RegattaPlateTexture(), tint.ToHtml(false), .3f, 1.3f))
                .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            itemEffects?.PlayEffect(model.Effects, 4, bolt, .62f);
            show.Projectile = bolt;
        }

        if (show.Projectile is not { } projectile) return;
        float t = Mathf.Clamp((show.Clock - show.LaunchedAt) / .62f, 0, 1);
        projectile.GlobalPosition = show.ReleasePoint.Lerp(show.Impact, t) + Vector3.Up * (1.25f * Mathf.Sin(t * Mathf.Pi));
        if (t < 1) return;
        projectile.QueueFree();
        show.Projectile = null;
        show.TargetFlinch = .46f;
        itemEffects?.PlayAt(model.Effects, 5, show.Impact);
    }
}
