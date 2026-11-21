using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const uint VoidCasterLightLayer = 1u << 13;
    private const uint VoidCastMotion = 0x4000002b;
    private const float VoidPrisonScale = 2.15f;
    private const float VoidCasterScale = 39f;
    private const float VoidFinaleDuration = 7.4f;
    private const int VoidChainCount = 4, VoidLinksPerChain = 19;
    private static readonly Vector3 VoidCasterHeading = new Vector3(1, 0, 1).Normalized();
    private static readonly Vector3 VoidCasterRest = Vector3.Up * 3.22f;
    private static readonly Vector3 VoidHorizonRest = Vector3.Up * 88 - VoidCasterHeading * 13;
    private static readonly Vector3 VoidShardLocal = Vector3.Up * 48;
    private static readonly Vector3 VoidFinaleShardFocus = VoidShardLocal * VoidPrisonScale;
    private static readonly RareCatalog.Creature HeraldShardLook = new()
    {
        Id = 8542, Name = "Shard of the Herald", Setup = 0x020008fc, Motion = 0x090000a3,
        Palette = 0x04000bef, Clothing = 0x10000193, PaletteTemplate = 39, Shade = .1f
    };
    private VoidSummoning? voidSummoning;

    private static class VoidUniform
    {
        public static readonly StringName Age = "age", Charge = "charge", Phase = "phase", Pulse = "pulse",
            ImpactActive = "impact_active", BindingStrength = "binding_strength", Surge = "surge", Palm = "palm",
            Aperture = "aperture", TentacleTip = "tentacle_tip", TipBend = "tip_bend", OrbBend = "orb_bend",
            OrbSocket = "orb_socket", Source = "source", Strength = "strength";
    }

    private sealed class VoidSummoning(Node3D root, AcActor caster, MeshInstance3D hole,
        ShaderMaterial horizon, Node3D streamRoot, ShaderMaterial[] streams, OmniLight3D coronaLight,
        MultiMeshInstance3D debris, VoidDebris[] orbits, MultiMeshInstance3D bindings,
        Node3D shard, ShaderMaterial seal)
    {
        internal readonly Node3D Root = root;
        internal readonly AcActor Caster = caster;
        internal readonly MeshInstance3D Hole = hole;
        internal readonly ShaderMaterial Horizon = horizon;
        internal readonly Node3D StreamRoot = streamRoot;
        internal readonly ShaderMaterial[] Streams = streams;
        internal readonly OmniLight3D CoronaLight = coronaLight;
        internal readonly MultiMeshInstance3D Debris = debris;
        internal readonly VoidDebris[] Orbits = orbits;
        internal readonly MultiMeshInstance3D Bindings = bindings;
        internal readonly Node3D Shard = shard;
        internal readonly ShaderMaterial Seal = seal;
        internal readonly Vector3 CasterRotation = caster.Rotation;
        internal float Age, PoseAge, NextCast = 3.8f, ClashPulse, FinaleAge = -1;
    }

    private readonly record struct VoidDebris(float Heading, float Radius, float Height, float Rate, Vector3 Size);

    private void BuildVoidSetpiece(Node3D root, Vector3 focus)
    {
        var stage = new Node3D { Name = "The Summoning", Position = focus, Scale = Vector3.One * VoidPrisonScale };
        root.AddChild(stage);
        var obsidian = BogCircuit.Paint("171526");
        var plinth = BogCircuit.MeshAt(stage,
            new CylinderMesh { BottomRadius = 26, TopRadius = 22, Height = 4.2f, RadialSegments = 10 },
            new Vector3(0, 1.12f, 0), Vector3.One, obsidian);
        plinth.Name = "Obsidian altar";
        plinth.Layers |= VoidCasterLightLayer;
        plinth.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var altarGlow = new StandardMaterial3D
        {
            AlbedoColor = new Color("604fbe"),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        BogCircuit.MeshAt(stage, new TorusMesh
        {
            InnerRadius = 19.8f, OuterRadius = 20.06f, Rings = 64, RingSegments = 5
        }, Vector3.Up * 3.29f, Vector3.One, altarGlow).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var look = RaceMount.Get(RaceMountKind.BaelZharon).Native
            ?? throw new InvalidDataException("The native Bael’Zharon appearance is missing.");
        var caster = assets.CreateActor(look.Setup, look.Motion, VoidCasterScale);
        stage.AddChild(caster);
        caster.Name = "Bael’Zharon";
        caster.Position = VoidCasterRest;
        caster.Face(VoidCasterHeading);
        caster.ApplyNativeAppearance(look);
        LightVoidActor(caster, VoidCasterLightLayer, new Color("b8a7ff"), .52f);
        caster.SetCombatStyle(AcActor.MagicStyle);
        caster.HoldFinalPose = true;
        caster.Play(VoidCastMotion, once: true);
        caster.TickCurrentMotion(.42f);

        var horizon = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/VoidEventHorizon.gdshader"),
            RenderPriority = 4
        };
        var hole = BogCircuit.MeshAt(stage, new QuadMesh { Size = new Vector2(166, 166) }, VoidHorizonRest, Vector3.One, horizon);
        hole.Name = "Event Horizon";
        hole.ExtraCullMargin = 84;
        hole.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        var ribbonShader = GD.Load<Shader>("res://Shaders/VoidCastingStream.gdshader");
        var ribbonMesh = BuildVoidCastingMesh();
        var streamRoot = new Node3D { Name = "Summoning streams" };
        stage.AddChild(streamRoot);
        var streams = new ShaderMaterial[2];
        for (int hand = 0; hand < streams.Length; hand++)
        {
            var material = new ShaderMaterial { Shader = ribbonShader };
            streams[hand] = material;
            var stream = BogCircuit.MeshAt(streamRoot, ribbonMesh, Vector3.Zero, Vector3.One, material);
            stream.Name = hand == 0 ? "Left casting stream" : "Right casting stream";
            stream.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        var key = AddVoidLight("Violet summoning light", new Color("aa9bff"), 4.1f, 109,
            VoidCasterHeading * 27 + Vector3.Up * 60);
        AddVoidLight("Amber ring light", new Color("ff9767"), 2.3f, 94, VoidHorizonRest - Vector3.Up * 7);
        AddVoidLight("Cool altar fill", new Color("6c98f0"), 2.05f, 77, VoidCasterHeading * 38 + Vector3.Up * 19);

        var debrisMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh
            {
                Radius = 1, Height = 2, RadialSegments = 5, Rings = 2, Material = obsidian
            },
            InstanceCount = 14,
            CustomAabb = new Aabb(new Vector3(-72, 0, -72), new Vector3(144, 124, 144))
        };
        var debris = new MultiMeshInstance3D
        {
            Name = "Caught debris", Multimesh = debrisMesh, Layers = VoidCasterLightLayer | 1u,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        stage.AddChild(debris);
        using var random = new RandomNumberGenerator { Seed = 0x0bae1 };
        var orbits = Enumerable.Range(0, debrisMesh.InstanceCount).Select(i => new VoidDebris(
            i * Mathf.Tau / debrisMesh.InstanceCount + random.RandfRange(-.13f, .13f),
            random.RandfRange(30, 59), random.RandfRange(20, 73), random.RandfRange(.19f, .43f),
            new Vector3(random.RandfRange(.8f, 2.1f), random.RandfRange(1.1f, 3.1f), random.RandfRange(.5f, 1.5f)))).ToArray();
        var bindings = BuildVoidRestraints(stage);
        var (shard, seal) = BuildVoidShard(stage);
        voidSummoning = new VoidSummoning(stage, caster, hole, horizon, streamRoot, streams, key, debris, orbits, bindings, shard, seal);
        DrawVoidSummoning(voidSummoning, 0);

        OmniLight3D AddVoidLight(string name, Color color, float energy, float range, Vector3 position)
        {
            var light = new OmniLight3D
            {
                Name = name, Position = position, LightColor = color, LightEnergy = energy, OmniRange = range * VoidPrisonScale,
                OmniAttenuation = 1.4f, LightCullMask = VoidCasterLightLayer
            };
            stage.AddChild(light);
            return light;
        }
    }

    private (Node3D Root, ShaderMaterial Seal) BuildVoidShard(Node3D stage)
    {
        var root = new Node3D { Name = "Shard of the Herald", Position = VoidShardLocal, Visible = false };
        stage.AddChild(root);
        var crystal = assets.CreateActor(HeraldShardLook.Setup, HeraldShardLook.Motion);
        root.AddChild(crystal);
        crystal.Name = "Native crystal";
        crystal.ApplyNativeAppearance(HeraldShardLook);
        LightVoidActor(crystal, VoidCasterLightLayer, new Color("c8e6ff"), .95f);
        Aabb? bounds = null;
        foreach (var (mesh, index) in crystal.BodyMeshBindings)
        {
            if (mesh.Mesh is not { } geometry || !mesh.Visible) continue;
            Aabb partBounds = (crystal.BodyPartTransform(index) * mesh.Transform) * geometry.GetAabb();
            bounds = bounds?.Merge(partBounds) ?? partBounds;
        }
        if (bounds is { } authoredBounds)
        {
            float size = Math.Max(authoredBounds.Size.Y, Math.Max(authoredBounds.Size.X, authoredBounds.Size.Z));
            float scale = 25 / Math.Max(.1f, size);
            crystal.Scale = Vector3.One * scale;
            crystal.Position = -(authoredBounds.GetCenter() * scale);
        }

        var seal = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://Shaders/VoidBindingSeal.gdshader"),
            RenderPriority = 5
        };
        var aura = BogCircuit.MeshAt(root, new QuadMesh { Size = new Vector2(76, 76) }, Vector3.Zero, Vector3.One, seal);
        aura.Name = "Asheron’s seal";
        aura.ExtraCullMargin = 40;
        aura.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return (root, seal);
    }

    private static MultiMeshInstance3D BuildVoidRestraints(Node3D stage)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true
        };
        var chainMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new TorusMesh
            {
                InnerRadius = .95f, OuterRadius = 1.47f, Rings = 14, RingSegments = 5,
                Material = material
            },
            InstanceCount = VoidChainCount * VoidLinksPerChain,
            CustomAabb = new Aabb(new Vector3(-40, -5, -40), new Vector3(80, 70, 80))
        };
        var chains = new MultiMeshInstance3D
        {
            Name = "Asheron’s binding chains", Multimesh = chainMesh, Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        stage.AddChild(chains);
        return chains;
    }

    private static void DrawVoidChains(MultiMeshInstance3D bindings, float age, float tightening, float absorption)
    {
        float falloff = 1 - Mathf.SmoothStep(4.4f, 5.2f, age);
        bindings.Visible = age > .12f && falloff > 0;
        if (!bindings.Visible) return;
        MultiMesh chainMesh = bindings.Multimesh;
        for (int chain = 0; chain < VoidChainCount; chain++)
        {
            float heading = (chain + .25f) * Mathf.Tau / VoidChainCount;
            Vector3 radial = new(MathF.Cos(heading), 0, MathF.Sin(heading));
            Vector3 chainPlaneNormal = radial.Cross(Vector3.Up);
            Vector3 start = radial * 36 + Vector3.Up * 1.7f;
            Vector3 end = radial * 7 + Vector3.Up * (50 + 5 * (chain % 2));
            end = end.Lerp(VoidShardLocal + radial * 6, absorption);
            float bow = Mathf.Lerp(15, 2.2f, tightening);
            float reach = Mathf.SmoothStep(.15f + chain * .16f, 1.48f + chain * .16f, age);
            for (int link = 0; link < VoidLinksPerChain; link++)
            {
                float t = link / (VoidLinksPerChain - 1f);
                Vector3 at = start.Lerp(end, t) + radial * (MathF.Sin(t * Mathf.Pi) * bow);
                Vector3 axis = (end - start + radial * (MathF.Cos(t * Mathf.Pi) * (Mathf.Pi * bow))).Normalized();
                Basis linkOrientation = Basis.LookingAt(axis, chainPlaneNormal);
                Basis basis = linkOrientation.Rotated(axis, (link % 2) * Mathf.Pi / 2);
                basis = basis * Basis.FromScale(new Vector3(.78f, 1, 1.6f));
                int index = chain * VoidLinksPerChain + link;
                float opacity = Mathf.SmoothStep(t * .90f, Math.Min(1, t * .90f + .1f), reach) * falloff;
                float flash = .79f + .21f * MathF.Sin(age * 12 - t * 19);
                chainMesh.SetInstanceColor(index, new Color(.52f + .36f * tightening, .83f, 1f - .27f * tightening, opacity * flash));
                chainMesh.SetInstanceTransform(index, new Transform3D(basis, at));
            }
        }
    }

    private void DrawVoidClashPulse(float pulse, float age)
    {
        if (voidSummoning is not { } scene || scene.FinaleAge >= 0 || (pulse == 0 && scene.ClashPulse == 0) ||
            !GodotObject.IsInstanceValid(scene.Root)) return;
        scene.ClashPulse = pulse;
        scene.Horizon.SetShaderParameter(VoidUniform.BindingStrength, pulse * (.24f + .06f * MathF.Sin(age * 9)));
    }

    private void ResetVoidFinaleVisuals()
    {
        if (voidSummoning is not { } scene || !GodotObject.IsInstanceValid(scene.Root)) return;
        scene.Bindings.Hide();
        scene.Shard.Hide();
        scene.Caster.Show();
        scene.Caster.Position = VoidCasterRest;
        scene.Caster.Rotation = scene.CasterRotation;
        scene.Caster.Scale = Vector3.One * VoidCasterScale;
        scene.Hole.Show();
        scene.Hole.Position = VoidHorizonRest;
        scene.Hole.Scale = Vector3.One;
        scene.StreamRoot.Show();
        scene.Debris.Show();
        scene.CoronaLight.Position = VoidCasterHeading * 27 + Vector3.Up * 60;
        scene.Horizon.SetShaderParameter(VoidUniform.BindingStrength, 0f);
        scene.ClashPulse = 0;
        scene.FinaleAge = -1;
        DrawVoidSummoning(scene, scene.Age);
    }

    private void DrawVoidFinale(float age)
    {
        if (voidSummoning is not { } scene || !GodotObject.IsInstanceValid(scene.Root)) return;
        age = Mathf.Clamp(age, 0, VoidFinaleDuration);
        float step = age - Math.Max(scene.FinaleAge, 0);
        scene.FinaleAge = age;
        float tightening = Mathf.SmoothStep(.9f, 2.38f, age);
        float absorption = Mathf.SmoothStep(2.38f, 4.7f, age);
        bool visible = absorption < 1;
        scene.Caster.Visible = visible;
        scene.Hole.Visible = visible;
        scene.StreamRoot.Visible = absorption < .9f;
        scene.Debris.Visible = visible;
        if (visible)
        {
            if (step > 0) scene.Caster.TickCurrentMotion(step * (1 - tightening * .8f));
            float struggle = MathF.Sin(age * 15) * tightening * (1 - absorption) * .095f;
            scene.Caster.Rotation = scene.CasterRotation + new Vector3(struggle, absorption * 2.2f, -struggle * .6f);
            scene.Caster.Position = VoidCasterRest.Lerp(VoidShardLocal, absorption);
            scene.Caster.Scale = Vector3.One * (VoidCasterScale * Mathf.Lerp(1, .025f, absorption));
            scene.Hole.Position = VoidHorizonRest.Lerp(VoidShardLocal, absorption);
            scene.Hole.Scale = Vector3.One * Mathf.Lerp(1, .035f, absorption);
            scene.Horizon.SetShaderParameter(VoidUniform.BindingStrength, tightening);
            DrawVoidSummoning(scene, scene.Age + age, absorption);
        }

        DrawVoidChains(scene.Bindings, age, tightening, absorption);
        float reveal = Mathf.SmoothStep(2.26f, 4.64f, age);
        scene.Shard.Visible = reveal > 0;
        if (scene.Shard.Visible)
        {
            scene.Shard.Scale = Vector3.One * Mathf.Lerp(.13f, 1, reveal);
            scene.Shard.Rotation = Vector3.Up * (age * .34f);
            float flash = 1 - Mathf.SmoothStep(.06f, .70f, Math.Abs(age - 4.72f));
            scene.Seal.SetShaderParameter(VoidUniform.Charge, reveal);
            scene.Seal.SetShaderParameter(VoidUniform.Pulse, flash);
            scene.Seal.SetShaderParameter(VoidUniform.Age, age);
            scene.CoronaLight.Position = VoidShardLocal + VoidCasterHeading * 16;
            scene.CoronaLight.LightEnergy = 3.65f + flash * 4.2f;
        }
    }

    private void TickVoidSetpiece(float dt)
    {
        if (voidSummoning is not { } scene) return;
        if (!GodotObject.IsInstanceValid(scene.Root) || scene.Root.IsQueuedForDeletion())
        {
            voidSummoning = null;
            return;
        }
        if (!scene.Root.IsVisibleInTree()) return;
        if (scene.FinaleAge >= 0) return;

        scene.Age += dt;
        float step = scene.Age - scene.PoseAge;
        if (step + .0005f < VoidPoseInterval(camera.Position, Vector3.Up * (43 * VoidPrisonScale))) return;
        scene.PoseAge = scene.Age;
        if (scene.Age >= scene.NextCast)
        {
            scene.Caster.HoldFinalPose = false;
            scene.Caster.TickCurrentMotion(.06f);
            scene.Caster.HoldFinalPose = true;
            scene.Caster.Play(VoidCastMotion, once: true);
            scene.NextCast = scene.Age + 3.8f;
        }
        scene.Caster.TickCurrentMotion(step * .82f);
        DrawVoidSummoning(scene, scene.Age);
    }

    // Distant actors can commit fewer native pose transforms without slowing the race clock.
    private static float VoidPoseInterval(Vector3 eye, Vector3 focus) =>
        eye.DistanceSquaredTo(focus) > 430 * 430 ? 1f / 15 : 1f / 30;

    private static void DrawVoidSummoning(VoidSummoning scene, float age, float absorption = 0)
    {
        float beat = .54f + .46f * MathF.Sin(age * 2.4f + .8f);
        scene.Horizon.SetShaderParameter(VoidUniform.Surge, beat);
        scene.CoronaLight.LightEnergy = 3.65f + beat * 1.1f;
        Vector3 casterRight = VoidCasterHeading.Cross(Vector3.Up);
        Transform3D casterPose = scene.Caster.Transform;
        float spread = (19 + 4.2f * MathF.Sin(age * 1.1f)) * (1 - absorption);
        for (int hand = 0; hand < scene.Streams.Length; hand++)
        {
            int side = hand == 0 ? -1 : 1;
            Vector3 origin = casterPose * scene.Caster.BodyPartTransform(hand == 0 ? 12 : 15).Origin;
            Vector3 target = scene.Hole.Position - Vector3.Up * (9 * (1 - absorption)) + casterRight * (side * spread);
            scene.Streams[hand].SetShaderParameter(VoidUniform.Palm, origin);
            scene.Streams[hand].SetShaderParameter(VoidUniform.Aperture, target);
            scene.Streams[hand].SetShaderParameter(VoidUniform.Surge, beat);
        }
        MultiMesh debris = scene.Debris.Multimesh;
        for (int i = 0; i < scene.Orbits.Length; i++)
        {
            var orbit = scene.Orbits[i];
            float angle = orbit.Heading + age * orbit.Rate + absorption * Mathf.Tau;
            Vector3 at = new(MathF.Cos(angle) * orbit.Radius,
                orbit.Height + MathF.Sin(angle * 1.4f) * 3.5f, MathF.Sin(angle) * orbit.Radius);
            Basis tumble = Basis.FromEuler(new Vector3(angle * .8f, angle, -angle * 1.1f)).Scaled(orbit.Size * (1 - absorption * .9f));
            debris.SetInstanceTransform(i, new Transform3D(tumble, at.Lerp(VoidShardLocal, absorption)));
        }
    }

    private static ArrayMesh BuildVoidCastingMesh()
    {
        const int segments = 32, strands = 3;
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int strand = 0; strand < strands; strand++)
        for (int segment = 0; segment <= segments; segment++)
        {
            int vertex = (strand * (segments + 1) + segment) * 2;
            float length = segment / (float)segments;
            for (int edge = 0; edge <= 1; edge++)
            {
                mesh.SetUV(new Vector2(length, edge));
                mesh.SetColor(new Color(strand / (float)strands, 0, 0));
                mesh.AddVertex(new Vector3(edge, length, strand));
            }
            if (segment == segments) continue;
            mesh.AddIndex(vertex); mesh.AddIndex(vertex + 2); mesh.AddIndex(vertex + 1);
            mesh.AddIndex(vertex + 1); mesh.AddIndex(vertex + 2); mesh.AddIndex(vertex + 3);
        }
        ArrayMesh ribbons = mesh.Commit();
        ribbons.CustomAabb = new Aabb(new Vector3(-58, 0, -58), new Vector3(116, 123, 116));
        return ribbons;
    }
}
