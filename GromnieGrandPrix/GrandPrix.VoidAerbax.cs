using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint VoidAerbaxLightLayer = 1u << 15;
    private const float VoidAerbaxOrbAdvance = 24f, VoidAerbaxOrbHeight = 31f, VoidAerbaxOrbRadius = 15.6f;
    private static readonly int[] AerbaxTentacleTips = [5, 16, 20, 29, 31, 34];
    private VoidAerbaxScene? voidAerbax;

    private sealed class VoidAerbaxScene(Node3D stage, AcActor caster, ShaderMaterial orb,
        ShaderMaterial[] tentacles, OmniLight3D redLight, MeshInstance3D seal)
    {
        public readonly Node3D Stage = stage;
        public readonly AcActor Caster = caster;
        public readonly ShaderMaterial Orb = orb;
        public readonly ShaderMaterial[] Tentacles = tentacles;
        public readonly OmniLight3D RedLight = redLight;
        public readonly MeshInstance3D Seal = seal;
        public float PreviousAge, PreviousCharge, PreviousPulse;
    }

    private static Vector3 VoidAerbaxOrigin(Vector3 position) => position + VoidSideStageScale *
        (new Vector3(-position.X, 0, -position.Z).Normalized() * VoidAerbaxOrbAdvance + Vector3.Up * VoidAerbaxOrbHeight);

    private void BuildVoidAerbax(Node3D root, Vector3 position)
    {
        Vector3 inward = new Vector3(-position.X, 0, -position.Z).Normalized();
        var stage = new Node3D { Name = "Aerbax's charging dais" };
        root.AddChild(stage);
        stage.Position = position;
        stage.Basis = Basis.LookingAt(inward).Scaled(Vector3.One * VoidSideStageScale);

        var rock = BogCircuit.Paint("201422");
        var platform = BogCircuit.MeshAt(stage, new CylinderMesh
        {
            TopRadius = 27, BottomRadius = 14, Height = 13, RadialSegments = 9
        }, Vector3.Down * 6.5f, Vector3.One, rock);
        platform.Name = "Obsidian dais";
        platform.Layers |= VoidAerbaxLightLayer;
        platform.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var sealPaint = new StandardMaterial3D
        {
            AlbedoColor = new Color("67192c"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        BogCircuit.MeshAt(stage, new TorusMesh { InnerRadius = 23.6f, OuterRadius = 24f, Rings = 64, RingSegments = 6 },
            Vector3.Up * .06f, Vector3.One, sealPaint).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var seal = BogCircuit.MeshAt(stage, new TorusMesh { InnerRadius = 23.4f, OuterRadius = 23.55f, Rings = 6, RingSegments = 5 },
            Vector3.Up * .19f, Vector3.One, sealPaint);
        seal.Name = "Charging seal";
        seal.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var caster = assets.CreateActor(AerbaxSetup, AerbaxMotion, 10.4f);
        stage.AddChild(caster);
        caster.Name = "Aerbax";
        caster.Position = Vector3.Up * 4.5f;
        LightVoidActor(caster, VoidAerbaxLightLayer, new Color("ff959e"), .58f);
        var rim = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidAerbaxRim.gdshader") };
        foreach (var part in caster.BodyMeshes) part.MaterialOverlay = rim;

        var orb = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidAerbaxOrb.gdshader"), RenderPriority = 3 };
        var orbPosition = new Vector3(0, VoidAerbaxOrbHeight, -VoidAerbaxOrbAdvance);
        var sphere = BogCircuit.MeshAt(stage, new QuadMesh { Size = new Vector2(60, 60) }, orbPosition, Vector3.One, orb);
        sphere.Name = "Red energy sphere";
        sphere.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        sphere.ExtraCullMargin = 43;

        var tentacleShader = GD.Load<Shader>("res://Shaders/VoidAerbaxTentacle.gdshader");
        var tentacleMesh = BuildVoidAerbaxTentacleMesh();
        var tentacles = new ShaderMaterial[AerbaxTentacleTips.Length];
        for (int i = 0; i < tentacles.Length; i++)
        {
            tentacles[i] = new ShaderMaterial { Shader = tentacleShader, RenderPriority = 2 };
            tentacles[i].SetShaderParameter("phase", i * .812f);
            var strand = BogCircuit.MeshAt(stage, tentacleMesh, Vector3.Zero, Vector3.One, tentacles[i]);
            strand.Name = $"Tentacle charging stream {i + 1}";
            strand.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        var redLight = AddLight("Red orb light", "ff3945", orbPosition, 3.1f, 71);
        AddLight("Crimson rim light", "e84558", new Vector3(19, 34, 18), 4.6f, 78);
        AddLight("Aerbax face light", "c289a0", new Vector3(-17, 18, -28), 2.5f, 64);
        voidAerbax = new VoidAerbaxScene(stage, caster, orb, tentacles, redLight, seal);
        DrawVoidAerbax(.2f, 0, 0);

        OmniLight3D AddLight(string name, string color, Vector3 at, float strength, float range)
        {
            var light = new OmniLight3D
            {
                Name = name, Position = at, LightColor = new Color(color), LightEnergy = strength,
                OmniRange = range * VoidSideStageScale, OmniAttenuation = 1.45f, LightCullMask = VoidAerbaxLightLayer
            };
            stage.AddChild(light);
            return light;
        }
    }

    private void DrawVoidAerbax(float charge, float pulse, float age, float interval = 0)
    {
        if (voidAerbax is not { } stage) return;
        float delta = age - stage.PreviousAge;
        if (interval > 0 && delta >= 0 && delta + .0005f < interval) return;
        if (!GodotObject.IsInstanceValid(stage.Stage) || stage.Stage.IsQueuedForDeletion()) return;
        float step = Mathf.Clamp(delta, 0, .15f);
        bool chargeChanged = charge != stage.PreviousCharge;
        bool pulseChanged = pulse != stage.PreviousPulse;
        if (delta < 0) stage.Caster.Play(0x41000003, false);
        if ((delta < 0 || stage.PreviousCharge < .63f) && charge >= .63f)
            stage.Caster.Cast(false, .82f);
        stage.Caster.Position = Vector3.Up * (4.5f + 1.3f * Mathf.Sin(age * .83f) + charge * 1.4f);
        stage.Caster.Tick(step, false, Vector3.Zero);
        stage.PreviousAge = age;
        stage.PreviousCharge = charge;
        stage.PreviousPulse = pulse;
        if (chargeChanged) stage.Orb.SetShaderParameter(VoidUniform.Charge, charge);
        if (pulseChanged) stage.Orb.SetShaderParameter(VoidUniform.Pulse, pulse);
        stage.Orb.SetShaderParameter(VoidUniform.Age, age);
        if (chargeChanged || pulseChanged) stage.RedLight.LightEnergy = 2.4f + 6.3f * charge + pulse * 3.1f;
        stage.Seal.Rotation = new Vector3(0, age * .15f, 0);

        var center = new Vector3(0, VoidAerbaxOrbHeight, -VoidAerbaxOrbAdvance);
        float radius = VoidAerbaxOrbRadius * Mathf.Lerp(.22f, 1, Mathf.SmoothStep(0, 1, charge));
        Transform3D casterPose = stage.Caster.Transform;
        for (int i = 0; i < stage.Tentacles.Length; i++)
        {
            int side = i < 3 ? -1 : 1, tier = i % 3 - 1;
            var material = stage.Tentacles[i];
            Vector3 tip = casterPose * (stage.Caster.BodyPartTransform(AerbaxTentacleTips[i]) * (Vector3.Down * .45f));
            material.SetShaderParameter(VoidUniform.TentacleTip, tip);
            material.SetShaderParameter(VoidUniform.TipBend, tip + new Vector3(side * 5f, 10.8f + tier * 3.2f, -13f));
            if (chargeChanged)
            {
                Vector3 axis = new Vector3(side * .84f, tier * .53f, .63f - tier * .25f).Normalized();
                Vector3 end = center + axis * (radius * .94f);
                Vector3 spread = new Vector3(side * (radius + 11f), tier * 8.2f + 7.4f, 6f - tier * 3.2f);
                material.SetShaderParameter(VoidUniform.OrbBend, center + spread);
                material.SetShaderParameter(VoidUniform.OrbSocket, end);
                material.SetShaderParameter(VoidUniform.Charge, charge);
            }
            if (pulseChanged) material.SetShaderParameter(VoidUniform.Pulse, pulse);
            material.SetShaderParameter(VoidUniform.Age, age);
        }
    }

    private static ArrayMesh BuildVoidAerbaxTentacleMesh()
    {
        const int sections = 30, sides = 9;
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int section = 0; section <= sections; section++)
        for (int side = 0; side <= sides; side++)
        {
            float along = section / (float)sections, around = side / (float)sides;
            mesh.SetUV(new Vector2(along, around));
            mesh.AddVertex(new Vector3(Mathf.Cos(around * Mathf.Tau), along, Mathf.Sin(around * Mathf.Tau)));
            if (section == sections || side == sides) continue;
            int index = section * (sides + 1) + side;
            int nextRing = index + sides + 1;
            mesh.AddIndex(index); mesh.AddIndex(nextRing); mesh.AddIndex(index + 1);
            mesh.AddIndex(index + 1); mesh.AddIndex(nextRing); mesh.AddIndex(nextRing + 1);
        }
        var tube = mesh.Commit();
        tube.CustomAabb = new Aabb(new Vector3(-68, -16, -60), new Vector3(136, 94, 94));
        return tube;
    }
}
