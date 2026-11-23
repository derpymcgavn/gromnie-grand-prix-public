using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint VoidAsheronLightLayer = 1u << 14;
    private const uint VoidGuardianDiamondLayer = 1u << 17;
    private const uint GuardianGreatsword = 0x020016d4, VoidGuardianSwordStyle = 0x8000003e;
    private const float VoidGuardianHeightRatio = 3.5f, VoidGuardianSwordRatio = .55f;
    private const float VoidAsheronSealAdvance = 15f, VoidAsheronSealHeight = 34f;
    private static readonly Vector3 VoidBindingSealCenter = new(0, VoidAsheronSealHeight, -VoidAsheronSealAdvance);
    private static readonly RareCatalog.Creature TowerGuardianLook = new()
    {
        Id = 35273, Name = "Tower Guardian", Setup = 0x020016d7, Motion = 0x09000186,
        Clothing = 0x1000049a, Palette = 0x06002b2e
    };
    private VoidAsheronScene? voidAsheron;

    private sealed class VoidAsheronScene(Node3D root, AcActor asheron, AcActor[] guardians, VoidGuardianBlade[] blades,
        ShaderMaterial seal, ShaderMaterial[] channels, OmniLight3D bindingLight, MeshInstance3D[] rings)
    {
        public readonly Node3D Root = root;
        public readonly AcActor Asheron = asheron;
        public readonly AcActor[] Guardians = guardians;
        public readonly VoidGuardianBlade[] Blades = blades;
        public readonly ShaderMaterial Seal = seal;
        public readonly ShaderMaterial[] Channels = channels;
        public readonly OmniLight3D BindingLight = bindingLight;
        public readonly MeshInstance3D[] Rings = rings;
        public float PreviousAge, PreviousCharge, PreviousPulse;
    }

    private static Vector3 VoidAsheronInward(Vector3 position) => new Vector3(-position.X, 0, -position.Z).Normalized();

    private static Vector3 VoidAsheronCastOrigin(Vector3 position) =>
        position + VoidSideStageScale * (VoidAsheronInward(position) * VoidAsheronSealAdvance + Vector3.Up * VoidAsheronSealHeight);

    private void BuildVoidAsheron(Node3D root, Vector3 position)
    {
        var stage = new Node3D { Name = "Asheron's binding circle", Position = position,
            Basis = Basis.LookingAt(VoidAsheronInward(position)).Scaled(Vector3.One * VoidSideStageScale) };
        root.AddChild(stage);

        var granite = BogCircuit.Paint("242927");
        var trim = BogCircuit.Paint("796952");
        var floorLine = new StandardMaterial3D { AlbedoColor = new Color("c9aa67"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var rimLight = new StandardMaterial3D { AlbedoColor = new Color("68abb3"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var rings = new MeshInstance3D[3];
        rings[0] = BuildAltar("Binding altar", Vector3.Zero, 22, 8, floorLine);
        var look = RaceMount.Get(RaceMountKind.Asheron).Native
            ?? throw new InvalidDataException("The native Asheron appearance is missing.");
        var asheron = BuildActor("Asheron", look.Setup, look.Motion, 16.2f, Vector3.Up * 2.2f, Vector3.Forward);
        asheron.ApplyNativeAppearance(look);
        Illuminate(asheron);
        asheron.SetCombatStyle(AcActor.MagicStyle);
        asheron.TickCurrentMotion(0);
        float asheronHeight = MeasureVoidActorBody(asheron).Size.Y * asheron.Scale.Y;

        var guardians = new AcActor[2];
        var blades = new VoidGuardianBlade[guardians.Length];
        var emberMesh = BuildVoidGuardianEmber();
        var diamond = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidGuardianDiamond.gdshader") };
        for (int i = 0; i < guardians.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            Vector3 at = new(side * 82, -1.4f, 26);
            rings[i + 1] = BuildAltar(i == 0 ? "West sentinel dais" : "East sentinel dais", at, 31, 15, rimLight);
            var guardian = BuildActor(i == 0 ? "Tower Guardian West" : "Tower Guardian East",
                TowerGuardianLook.Setup, TowerGuardianLook.Motion, 1, at, new Vector3(-side * .28f, 0, -1));
            guardian.ApplyNativeAppearance(TowerGuardianLook);
            guardian.SetCombatStyle(VoidGuardianSwordStyle);
            guardian.TickCurrentMotion(0);
            var body = MeasureVoidActorBody(guardian);
            float scale = asheronHeight * VoidGuardianHeightRatio / body.Size.Y;
            guardian.Scale = Vector3.One * scale;
            guardian.Position = at + Vector3.Up * (.31f - body.Position.Y * scale);
            guardian.Hold(GuardianGreatsword, 1, scale: body.Size.Y * VoidGuardianSwordRatio / VoidGuardianSwordLength);
            foreach (var mesh in guardian.BodyMeshes)
            {
                mesh.Layers = VoidGuardianDiamondLayer;
                mesh.MaterialOverride = diamond;
            }
            int grip = guardian.Setup.Holdings[1].Part;
            var greatsword = guardian.Part(grip).GetChildren().OfType<AcActor>().First();
            greatsword.Name = "Greatsword of Flame and Light";
            Illuminate(greatsword);
            blades[i] = BuildVoidGuardianBlade(stage, guardian, grip, greatsword, emberMesh, i);
            guardians[i] = guardian;
        }

        var seal = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidBindingSeal.gdshader"), RenderPriority = 3 };
        var glyph = BogCircuit.MeshAt(stage, new QuadMesh { Size = new Vector2(41, 41) }, VoidBindingSealCenter, Vector3.One, seal);
        glyph.Name = "Binding seal";
        glyph.Layers |= VoidReflectionSceneryLayer;
        glyph.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var flowMesh = BuildVoidAsheronChannel();
        var flowShader = GD.Load<Shader>("res://Shaders/VoidBindingChannel.gdshader");
        var channels = new ShaderMaterial[4];
        for (int i = 0; i < channels.Length; i++)
        {
            var material = new ShaderMaterial { Shader = flowShader, RenderPriority = 2 };
            channels[i] = material;
            material.SetShaderParameter("phase", i * 1.71f);
            material.SetShaderParameter("tint", new Color(i < 2 ? "ffe2a0" : "7df0f2"));
            bool mage = i < 2;
            float side = i % 2 == 0 ? -1 : 1;
            material.SetShaderParameter("target", VoidBindingSealCenter + new Vector3(side * (mage ? 3.4f : 7.5f), mage ? -2.4f : -.4f, 0));
            material.SetShaderParameter("bow", new Vector3(side * (mage ? 2.4f : 4.7f), mage ? 2.2f : 4.8f, mage ? -2.4f : -4));
            var strand = BogCircuit.MeshAt(stage, flowMesh, Vector3.Zero, Vector3.One, material);
            strand.Name = i < 2 ? "Asheron's channel" : "Guardian channel";
            strand.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        var bindingLight = AddLight("Light of the binding", "ffe3b1", VoidBindingSealCenter, 3.2f, 82);
        AddLight("Asheron face light", "fff4d7", new Vector3(8, 18, -31), 4.5f, 81);
        AddLight("Sentinel rim light", "60c5e8", new Vector3(-8, 87, -12), 5.4f, 139);
        voidAsheron = new VoidAsheronScene(stage, asheron, guardians, blades, seal, channels, bindingLight, rings);
        DrawVoidAsheron(.18f, 0, 0);

        MeshInstance3D BuildAltar(string name, Vector3 at, float radius, float height, StandardMaterial3D glow)
        {
            MeshInstance3D rock = BogCircuit.MeshAt(stage, new CylinderMesh
            {
                TopRadius = radius, BottomRadius = radius * .63f, Height = height, RadialSegments = 8
            }, at + Vector3.Down * (height * .5f), Vector3.One, granite);
            rock.Name = name;
            rock.Layers |= VoidAsheronLightLayer;
            rock.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            MeshInstance3D cap = BogCircuit.MeshAt(stage, new CylinderMesh
            {
                TopRadius = radius * .83f, BottomRadius = radius * .89f, Height = .42f, RadialSegments = 8
            }, at + Vector3.Up * .10f, Vector3.One, trim);
            cap.Layers |= VoidAsheronLightLayer;
            cap.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var inlay = BogCircuit.MeshAt(stage, new TorusMesh
            {
                InnerRadius = radius * .68f, OuterRadius = radius * .70f, Rings = 8, RingSegments = 5
            }, at + Vector3.Up * .37f, Vector3.One, glow);
            inlay.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            return inlay;
        }

        AcActor BuildActor(string name, uint setup, uint motion, float scale, Vector3 at, Vector3 towards)
        {
            var actor = assets.CreateActor(setup, motion, scale);
            stage.AddChild(actor);
            actor.Name = name;
            actor.Position = at;
            actor.Face(towards);
            return actor;
        }

        static void Illuminate(AcActor actor) =>
            LightVoidActor(actor, VoidAsheronLightLayer, new Color("fff0c8"), .53f);

        OmniLight3D AddLight(string name, string color, Vector3 at, float strength, float range)
        {
            var light = new OmniLight3D
            {
                Name = name, Position = at, LightColor = new Color(color), LightEnergy = strength,
                OmniRange = range * VoidSideStageScale, OmniAttenuation = 1.28f,
                LightCullMask = VoidAsheronLightLayer | VoidGuardianDiamondLayer
            };
            stage.AddChild(light);
            return light;
        }
    }

    private void DrawVoidAsheron(float charge, float pulse, float age, float interval = 0)
    {
        if (voidAsheron is not { } scene) return;
        const uint idle = 0x41000003;
        const float asheronCue = .42f, firstGuardianCue = .44f, secondGuardianCue = .64f;
        float elapsed = age - scene.PreviousAge;
        if (interval > 0 && elapsed >= 0 && elapsed + .0005f < interval) return;
        if (!GodotObject.IsInstanceValid(scene.Root) || scene.Root.IsQueuedForDeletion()) return;
        float step = Mathf.Clamp(elapsed, 0, .15f);
        bool chargeChanged = charge != scene.PreviousCharge;
        bool pulseChanged = pulse != scene.PreviousPulse;
        if (elapsed < 0)
        {
            scene.Asheron.Play(idle, false);
            foreach (var guardian in scene.Guardians) guardian.Play(idle, false);
            scene.PreviousCharge = 0;
        }
        bool cast = charge >= asheronCue && scene.PreviousCharge < asheronCue && scene.Asheron.Cast(false, .51f);
        bool firstStrike = charge >= firstGuardianCue && scene.PreviousCharge < firstGuardianCue && scene.Guardians[0].AttackWeapon("sword");
        bool secondStrike = charge >= secondGuardianCue && scene.PreviousCharge < secondGuardianCue && scene.Guardians[1].AttackWeapon("sword");
        scene.Asheron.Position = Vector3.Up * (2.2f + .65f * Mathf.Sin(age * .92f) + charge * 1.06f);
        // Race previews can jump directly into the charge phase.
        bool seek = Mathf.Abs(elapsed) > .15f;
        scene.Asheron.TickCurrentMotion(cast && seek ? (charge - asheronCue) * 1.7f : step);
        scene.Guardians[0].TickCurrentMotion(firstStrike && seek ? (charge - firstGuardianCue) * 1.6f : step);
        scene.Guardians[1].TickCurrentMotion(secondStrike && seek ? (charge - secondGuardianCue) * 1.6f : step);
        scene.Blades[0].Draw(charge >= firstGuardianCue || pulse > .035f, elapsed < 0);
        scene.Blades[1].Draw(charge >= secondGuardianCue || pulse > .035f, elapsed < 0);
        scene.PreviousAge = age;
        scene.PreviousCharge = charge;
        scene.PreviousPulse = pulse;

        if (chargeChanged || pulseChanged) scene.BindingLight.LightEnergy = 2.65f + 5.6f * charge + 3.25f * pulse;
        scene.Seal.SetShaderParameter(VoidUniform.Age, age);
        if (chargeChanged) scene.Seal.SetShaderParameter(VoidUniform.Charge, charge);
        if (pulseChanged) scene.Seal.SetShaderParameter(VoidUniform.Pulse, pulse);
        for (int i = 0; i < scene.Rings.Length; i++)
        {
            scene.Rings[i].Rotation = Vector3.Up * (age * (i == 1 ? -.16f : .13f) + i * .23f);
            if (chargeChanged) scene.Rings[i].Scale = Vector3.One * (1 + charge * (.12f + .035f * i));
        }
        for (int i = 0; i < scene.Channels.Length; i++)
        {
            bool asheron = i < 2;
            Vector3 origin = asheron ? scene.Asheron.Transform * scene.Asheron.BodyPartTransform(i == 0 ? 12 : 15).Origin
                : scene.Blades[i - 2].Tip;
            ShaderMaterial stream = scene.Channels[i];
            stream.SetShaderParameter(VoidUniform.Source, origin);
            stream.SetShaderParameter(VoidUniform.Age, age);
            if (chargeChanged || pulseChanged)
                stream.SetShaderParameter(VoidUniform.Strength, .16f + (asheron ? .76f : .57f) * charge + .52f * pulse);
        }
    }

    private static Aabb MeasureVoidActorBody(AcActor actor)
    {
        Aabb? bounds = null;
        foreach (var (mesh, part) in actor.BodyMeshBindings)
        {
            if (!mesh.Visible || mesh.Mesh is not { } geometry) continue;
            var partBounds = (actor.BodyPartTransform(part) * mesh.Transform) * geometry.GetAabb();
            bounds = bounds?.Merge(partBounds) ?? partBounds;
        }
        return bounds ?? throw new InvalidDataException($"{actor.Name} has no body meshes.");
    }

    private static ArrayMesh BuildVoidAsheronChannel()
    {
        const int steps = 32;
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i <= steps; i++)
        {
            float along = i / (float)steps;
            mesh.SetUV(new Vector2(along, 0));
            mesh.AddVertex(new Vector3(along, 0, 0));
            mesh.SetUV(new Vector2(along, 1));
            mesh.AddVertex(new Vector3(along, 1, 0));
            if (i == steps) continue;
            int left = i * 2, next = left + 2;
            mesh.AddIndex(left); mesh.AddIndex(next); mesh.AddIndex(left + 1);
            mesh.AddIndex(left + 1); mesh.AddIndex(next); mesh.AddIndex(next + 1);
        }
        var channel = mesh.Commit();
        channel.CustomAabb = new Aabb(new Vector3(-200, -35, -105), new Vector3(400, 235, 230));
        return channel;
    }
}
