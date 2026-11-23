using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private sealed record VoidHerald(Node3D Root, Node3D Captive, AcActor Actor, ShaderMaterial Seal, ShaderMaterial Smoke);

    private VoidHerald BuildVoidShard(Node3D stage, RareCatalog.Creature baelLook)
    {
        var root = new Node3D { Name = "Shard of the Herald", Position = VoidShardLocal, Visible = false };
        stage.AddChild(root);
        var crystal = assets.CreateActor(HeraldShardLook.Setup, HeraldShardLook.Motion);
        root.AddChild(crystal);
        crystal.Name = "Native crystal";
        crystal.ApplyNativeAppearance(HeraldShardLook);
        crystal.SetShadowCasting(false);
        foreach (var mesh in crystal.BodyMeshes)
        {
            if (mesh.Mesh is not { } geometry || !mesh.Visible) continue;
            mesh.Layers = VoidCrystalLayer;
            for (int surface = 0; surface < geometry.GetSurfaceCount(); surface++)
            {
                var glass = CreateVoidCrystalGlass(mesh.GetActiveMaterial(surface) as StandardMaterial3D);
                glass.SetShaderParameter("crystal_tint", new Color("e3a0b5"));
                mesh.SetSurfaceOverrideMaterial(surface, glass);
            }
        }

        Aabb crystalBounds = MeasureVoidActorBody(crystal);
        float shellScale = 25 / Math.Max(.1f, Math.Max(crystalBounds.Size.Y, Math.Max(crystalBounds.Size.X, crystalBounds.Size.Z)));
        Vector3 shellSize = crystalBounds.Size * shellScale;
        crystal.Scale = Vector3.One * shellScale;
        crystal.Position = -crystalBounds.GetCenter() * shellScale;

        var fracturedGlass = CreateVoidCrystalGlass();
        fracturedGlass.SetShaderParameter("crystal_tint", new Color("c76d94"));
        var fractures = BogCircuit.MeshAt(root, BuildHeraldFractures(shellSize), Vector3.Zero, Vector3.One, fracturedGlass);
        fractures.Name = "Splintered facets";
        fractures.Layers = VoidCrystalLayer;
        fractures.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var captive = new Node3D { Name = "Trapped Bael’Zharon", Visible = false };
        root.AddChild(captive);
        var miniature = assets.CreateActor(baelLook.Setup, baelLook.Motion);
        captive.AddChild(miniature);
        miniature.Name = "Native Bael’Zharon";
        miniature.ApplyNativeAppearance(baelLook);
        LightVoidActor(miniature, VoidCasterLightLayer, new Color("ffb9a6"), 1.18f);
        miniature.Face(VoidCasterHeading);
        miniature.SetCombatStyle(AcActor.MagicStyle);
        miniature.HoldFinalPose = true;
        miniature.Play(VoidCastMotion, once: true);
        miniature.TickCurrentMotion(.32f);
        Aabb body = MeasureVoidActorBody(miniature);
        float width = new Vector2(body.Size.X, body.Size.Z).Length();
        float fit = Math.Min(shellSize.Y * .59f / Math.Max(.1f, body.Size.Y),
            Math.Min(shellSize.X, shellSize.Z) * .80f / Math.Max(.1f, width));
        miniature.Scale = Vector3.One * fit;
        miniature.Position = -(miniature.Basis.Orthonormalized() * body.GetCenter()) * fit;

        var smoke = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/VoidHeraldSmoke.gdshader"),
            RenderPriority = -1
        };
        smoke.SetShaderParameter("origin_radius", Math.Min(shellSize.X, shellSize.Z) * .40f);
        var wisps = BogCircuit.MeshAt(root, BuildHeraldSmokeCards(), Vector3.Zero, Vector3.One, smoke);
        wisps.Name = "Crimson flame smoke";
        wisps.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var seal = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/VoidBindingSeal.gdshader"),
            RenderPriority = 5
        };
        var aura = BogCircuit.MeshAt(root, new QuadMesh { Size = new Vector2(62, 62) }, Vector3.Zero, Vector3.One, seal);
        aura.Name = "Asheron’s seal";
        aura.ExtraCullMargin = 40;
        aura.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return new VoidHerald(root, captive, miniature, seal, smoke);
    }

    private static ArrayMesh BuildHeraldFractures(Vector3 shellSize)
    {
        var rim = new Vector2(shellSize.X, shellSize.Z) * .5f;
        (float Angle, float Height, float Out, float Rise, float Width)[] splinters =
        [
            (.12f, -.19f, 4.6f, -6.7f, 1.45f), (.66f, .26f, 4.0f, 6.4f, 1.08f),
            (1.47f, .11f, 3.2f, 8.9f, 1.26f), (2.38f, -.12f, 4.4f, -5.1f, 1.48f),
            (2.79f, .34f, 2.6f, 7.1f, .96f), (3.53f, .18f, 4.8f, 2.1f, 1.56f),
            (4.14f, -.29f, 2.5f, -6.5f, .81f), (4.86f, .25f, 3.8f, 7.8f, 1.22f),
            (5.53f, -.09f, 4.0f, -3.5f, 1.36f)
        ];
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        mesh.SetSmoothGroup(uint.MaxValue);
        const int sides = 5;
        foreach (var (angle, height, distance, rise, width) in splinters)
        {
            Vector3 side = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 basePoint = new(side.X * rim.X * .52f, height * shellSize.Y, side.Z * rim.Y * .52f);
            Vector3 tip = basePoint + side * distance + Vector3.Up * rise;
            Vector3 axis = (tip - basePoint).Normalized();
            Basis cross = Basis.LookingAt(axis, Vector3.Up);
            Vector3 collar = basePoint.Lerp(tip, .26f);
            Vector3[] ring = Enumerable.Range(0, sides).Select(i =>
            {
                float turn = i * Mathf.Tau / sides + angle;
                float radius = width * (i % 2 == 0 ? 1 : .68f);
                return collar + (cross.X * Mathf.Cos(turn) + cross.Y * Mathf.Sin(turn)) * radius
                    + axis * (.29f * Mathf.Sin(turn * 2));
            }).ToArray();
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % sides];
                Vector3 outwards = (a + b) * .5f - collar;
                Face(tip, a, b, outwards);
                Face(basePoint, a, b, outwards);
            }
        }
        mesh.GenerateNormals();
        return mesh.Commit();

        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if ((c - a).Cross(b - a).Dot(outward) < 0) (b, c) = (c, b);
            mesh.AddVertex(a);
            mesh.AddVertex(b);
            mesh.AddVertex(c);
        }
    }

    private static ArrayMesh BuildHeraldSmokeCards()
    {
        const int cards = 18;
        Vector2[] corners = [new(0, 0), new(0, 1), new(1, 1), new(0, 0), new(1, 1), new(1, 0)];
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < cards; i++)
        {
            float phase = Mathf.PosMod(i * .618034f, 1);
            foreach (Vector2 corner in corners)
            {
                mesh.SetUV(corner);
                mesh.SetUV2(new Vector2(phase, i / (float)cards));
                mesh.AddVertex(new Vector3(corner.X - .5f, .5f - corner.Y, 0));
            }
        }
        var cardsMesh = mesh.Commit();
        cardsMesh.CustomAabb = new Aabb(new(-23, -21, -23), new(46, 59, 46));
        return cardsMesh;
    }
}
