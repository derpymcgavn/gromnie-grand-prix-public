using Godot;
using DerethSurvivors;
using System.Runtime.CompilerServices;

public sealed partial class MountedRacer
{
    // Copper Cog, Gold Gear, and Silver Scope Knight armor surfaces, plus the loose Gear Knight head.
    private const uint CopperCogArmor = 0x08001558, GoldGearArmor = 0x08001562, SilverScopeArmor = 0x08001559;
    private const uint GearKnightHead = 0x010042a2, SalvagedGear = 0x01002956;
    private static readonly Vector3 JetSkiSaddlePosition = new(0, .74f, .31f);
    private static readonly Vector3 JetSkiHandle = new(0, 1.22f, -.53f);
    private static readonly ConditionalWeakTable<AcAssets, JetSkiResources> JetSkiParts = new();
    private static readonly float JetSkiSeatScale = RaceMount.Get(RaceMountKind.Gromnie).Scale;
    private Node3D? jetSkiRig, builtJetSkiRig, jetSkiSteering;
    private JetSkiWake? jetSkiWake;
    private float retiredWakeSeconds, retiredWakeTime;
    internal Node3D? JetSkiVisual => builtJetSkiRig;
    internal bool UsesJetSki => jetSkiRig is not null;
    private const float SkiFootClearance = .055f;

    private float SkiSurfaceOffset(Vector3 point) =>
        RegattaSurface.HeightOffset(new Vector2(point.X, point.Z), SkiWaterTime, skiFluid);

    private readonly record struct SkiSection(float Z, float HalfWidth, float Bottom, float Top);

    private sealed class JetSkiResources(AcAssets assets)
    {
        public readonly ArrayMesh Hull = SkiLoft(
            [new(-2.15f, .045f, .23f, .41f), new(-1.86f, .33f, .035f, .42f),
             new(-1.21f, .61f, -.16f, .40f), new(-.54f, .77f, -.29f, .34f),
             new(.18f, .82f, -.30f, .31f), new(.94f, .77f, -.25f, .28f),
             new(1.65f, .66f, -.18f, .23f), new(1.85f, .53f, -.11f, .20f)]);
        public readonly ArrayMesh Fairing = SkiLoft(
            [new(-1.96f, .075f, .32f, .48f), new(-1.62f, .34f, .31f, .58f),
             new(-1.05f, .49f, .25f, .76f), new(-.50f, .48f, .25f, .80f),
             new(.05f, .40f, .24f, .64f), new(.66f, .38f, .22f, .49f),
             new(1.32f, .32f, .20f, .34f), new(1.70f, .27f, .18f, .28f)]);
        public readonly ArrayMesh Seat = SkiLoft(
            [new(-.18f, .23f, .57f, .61f), new(.06f, .27f, .55f, .68f),
             new(.43f, .27f, .46f, .70f), new(.83f, .26f, .41f, .72f),
             new(1.04f, .22f, .34f, .64f)]);
        public readonly StandardMaterial3D[] Panels =
            [SkiArmor(assets, CopperCogArmor, "b98961"), SkiArmor(assets, GoldGearArmor, "cfa64e"),
             SkiArmor(assets, SilverScopeArmor, "aabfc9"), SkiArmor(assets, CopperCogArmor, "80aab7"),
             SkiArmor(assets, SilverScopeArmor, "9f9abd")];
        public readonly StandardMaterial3D Underside = SkiArmor(assets, SilverScopeArmor, "535f61");
        public readonly StandardMaterial3D Trim = SkiArmor(assets, GoldGearArmor, "dec691");
        public readonly StandardMaterial3D Pad = SkiGrip(assets);
        public readonly StandardMaterial3D Glow = SkiLamp(assets);
        public readonly ShaderMaterial Wake = CreateJetSkiWakeMaterial(assets);
        public readonly ShaderMaterial Spray = CreateRegattaSprayMaterial(
            assets.Archive.Entries.ContainsKey(RegattaSpraySprite) ? assets.Texture(RegattaSpraySprite) : null);
    }

    private void BuildJetSki(AcAssets assets, int color, bool active)
    {
        var parts = JetSkiParts.GetValue(assets, static archive => new JetSkiResources(archive));
        var optical = parts.Panels[(int)((uint)color % (uint)parts.Panels.Length)];
        var hull = new Node3D { Name = "Gear Knight jet ski", Visible = active };
        builtJetSkiRig = hull;
        if (active) jetSkiRig = hull;
        Root.AddChild(hull);

        BogCircuit.MeshAt(hull, parts.Hull, Vector3.Zero, Vector3.One, parts.Underside).Name = "Planing hull";
        BogCircuit.MeshAt(hull, parts.Fairing, Vector3.Zero, Vector3.One, optical).Name = "Gear Knight armor fairing";
        BogCircuit.MeshAt(hull, parts.Seat, Vector3.Zero, Vector3.One, parts.Pad).Name = "Padded rider seat";

        if (assets.Archive.Entries.ContainsKey(GearKnightHead))
        {
            var prow = new MeshInstance3D
            {
                Name = "Salvaged Gear Knight figurehead", Mesh = assets.Mesh(GearKnightHead),
                Position = new Vector3(0, .62f, -1.40f), Scale = Vector3.One * .96f
            };
            hull.AddChild(prow);
        }

        foreach (int side in new[] { -1, 1 })
        {
            var rail = BogCircuit.Box(hull, new(side * .66f, .30f, .38f), new(.12f, .10f, 1.37f), parts.Trim);
            rail.Rotation = new Vector3(-.07f, -side * .11f, 0);
            BogCircuit.Box(hull, new(side * .49f, .311f, .34f), new(.26f, .067f, 1.25f), parts.Pad);
            var aetherium = BogCircuit.Box(hull, new(side * .48f, .56f, -.81f), new(.075f, .09f, .47f), parts.Glow);
            aetherium.Rotation = new Vector3(-.10f, side * .13f, 0);
        }

        var nozzle = new CylinderMesh { TopRadius = .28f, BottomRadius = .34f, Height = .34f, RadialSegments = 12, Rings = 1 };
        BogCircuit.MeshAt(hull, nozzle, new(0, .145f, 1.75f), Vector3.One, parts.Trim).RotationDegrees = new(90, 0, 0);
        var throat = new CylinderMesh { TopRadius = .18f, BottomRadius = .18f, Height = .041f, RadialSegments = 12, Rings = 1 };
        BogCircuit.MeshAt(hull, throat, new(0, .145f, 1.925f), Vector3.One, parts.Glow).RotationDegrees = new(90, 0, 0);
        if (assets.Archive.Entries.ContainsKey(SalvagedGear))
            hull.AddChild(new MeshInstance3D
            {
                Name = "Gear Knight impeller", Mesh = assets.Mesh(SalvagedGear),
                Position = new(0, .145f, 1.96f), Scale = Vector3.One * 1.36f, RotationDegrees = new(90, 0, 0)
            });

        var column = new CylinderMesh { TopRadius = .089f, BottomRadius = .16f, Height = .53f, RadialSegments = 10, Rings = 1 };
        var steeringColumn = BogCircuit.MeshAt(hull, column, new(0, .99f, -.65f), Vector3.One, parts.Trim);
        steeringColumn.RotationDegrees = new(16, 0, 0);
        jetSkiSteering = new Node3D { Name = "Steering bar", Position = JetSkiHandle };
        hull.AddChild(jetSkiSteering);
        var bar = BogCircuit.Box(jetSkiSteering, Vector3.Zero, new(1.04f, .072f, .08f), parts.Trim);
        bar.Name = "Handlebars";
        foreach (int side in new[] { -1, 1 })
            BogCircuit.Box(jetSkiSteering, new(side * .45f, .012f, .015f), new(.24f, .089f, .11f), parts.Pad);
        BogCircuit.Box(jetSkiSteering, new(0, .044f, -.03f), new(.24f, .046f, .16f), parts.Glow);

        if (DisplayServer.GetName() != "headless") jetSkiWake = new JetSkiWake(Root, parts.Wake, parts.Spray);
    }

    internal bool SetJetSkiActive(bool active)
    {
        if (UsesJetSki == active || builtJetSkiRig is not { } hull || Flight is not null || Rider.TopLevel) return false;
        if (overboostDetached) RestoreOverboostPose(reseat: true);
        if (!active)
        {
            retiredWakeSeconds = 2.5f;
            retiredWakeTime = SkiWaterTime;
        }
        else retiredWakeSeconds = 0;
        hull.Hide();
        hull.Transform = Transform3D.Identity;
        jetSkiRig = active ? hull : null;
        Saddle.Reparent(active ? hull : groundSaddleParent, false);
        if (groundSaddleTack is { } tack) tack.Visible = !active;
        riderScale = flightRiderScale * (active ? JetSkiSeatScale : relativeScale);
        Rider.Scale = Vector3.One * riderScale;
        PoseMounted();
        CaptureMountedArms();
        UpdateSaddle(0);
        roadPoseLift = 0;
        Array.Clear(DustContact);
        ResetJetSkiWater();
        SetMountVisible(PowerForm is null);
        return true;
    }

    private void AgeJetSkiWake(BogCircuit track, float dt)
    {
        if (retiredWakeSeconds <= 0 || builtJetSkiRig is not { } hull ||
            jetSkiWake is null || track.WaterCourseLevel is not { } water) return;
        retiredWakeSeconds -= dt;
        retiredWakeTime += dt;
        jetSkiWake.Update(dt, hull.GlobalTransform, water, retiredWakeTime, 0, 0, 0, false, false, 0, null);
    }

    private void PoseJetSkiRider()
    {
        PoseSeated(JetSkiSeatScale, .96f, legPitch: -.10f);
        if (Rider.Setup.Parts.Length < 16) return;
        Grip(10);
        Grip(13);
        Rider.RefreshBodyPoseCache();

        void Grip(int shoulderIndex)
        {
            var upper = Rider.Part(shoulderIndex);
            var lower = Rider.Part(shoulderIndex + 1);
            var palm = Rider.Part(shoulderIndex + 2);
            var arm = lower.Position - upper.Position;
            var forearm = palm.Position - lower.Position;
            float armLength = arm.Length(), forearmLength = forearm.Length();
            if (Math.Min(armLength, forearmLength) < .01f) return;

            float side = Math.Sign(upper.Position.X);
            Vector3 grip = JetSkiHandle + Vector3.Right * (side * .45f) - JetSkiSaddlePosition;
            grip = new Basis(Vector3.Right, .1f) * grip;
            var reach = (grip - Rider.Position) / Rider.Scale.Y - upper.Position;
            if (reach.LengthSquared() < .0001f) return;
            var forward = reach.Normalized();
            var outward = new Vector3(side * .65f, -1, 0);
            outward = (outward - forward * outward.Dot(forward)).Normalized();
            float length = Mathf.Clamp(reach.Length(), Math.Abs(armLength - forearmLength) + .005f, armLength + forearmLength - .005f);
            float along = (armLength * armLength - forearmLength * forearmLength + length * length) / (2 * length);
            Vector3 elbow = upper.Position + forward * along + outward * MathF.Sqrt(Math.Max(0, armLength * armLength - along * along));
            Vector3 wrist = upper.Position + forward * length;
            var upperTurn = new Basis(new Quaternion(arm.Normalized(), (elbow - upper.Position).Normalized()).Normalized());
            var lowerTurn = new Basis(new Quaternion(forearm.Normalized(), (wrist - elbow).Normalized()).Normalized());
            upper.Basis = upperTurn * upper.Basis;
            lower.Transform = new Transform3D(lowerTurn * lower.Basis, elbow);
            palm.Transform = new Transform3D(lowerTurn * palm.Basis, wrist);
        }
    }

    private void UpdateJetSkiWake(float dt, float steering, float waterLevel)
    {
        if (jetSkiRig is not { } ski) return;
        if (jetSkiWake is null || !Root.IsInsideTree()) return;
        float contact = !MountUnavailable && ski.Visible && Speed > 2.3f && LeapHeight < .11f && !SkiAirborne &&
            EjectTime <= 0 && ResetHold <= 0 ? SkiSternContact : 0;
        jetSkiWake.Update(dt, ski.GlobalTransform, waterLevel, SkiWaterTime, Speed, steering, Lean,
            GroundDrifting, Boost > 0, contact, skiFluid);
    }

    private static StandardMaterial3D SkiArmor(AcAssets assets, uint surface, string baseColor)
    {
        var armor = assets.Archive.Entries.ContainsKey(surface) && assets.Surface(surface) is { AlbedoTexture: not null } native
            ? (StandardMaterial3D)native.Duplicate() : new StandardMaterial3D();
        armor.AlbedoColor = new Color(baseColor).Lerp(Colors.White, armor.AlbedoTexture is null ? 0 : .36f);
        armor.CullMode = BaseMaterial3D.CullModeEnum.Back;
        armor.Roughness = .58f;
        armor.Metallic = .48f;
        armor.MetallicSpecular = .28f;
        armor.SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx;
        return armor;
    }

    private static StandardMaterial3D SkiGrip(AcAssets assets)
    {
        var material = SkiArmor(assets, CopperCogArmor, "312c29");
        material.AlbedoColor = new Color("433730");
        material.Metallic = .02f;
        material.Roughness = .91f;
        return material;
    }

    private static StandardMaterial3D SkiLamp(AcAssets assets)
    {
        var material = SkiArmor(assets, SilverScopeArmor, "327f8c");
        material.AlbedoColor = new Color("448697");
        material.EmissionEnabled = true;
        material.Emission = new Color("67ebee");
        material.EmissionTexture = material.AlbedoTexture;
        material.EmissionEnergyMultiplier = 1.35f;
        material.Metallic = .12f;
        material.Roughness = .40f;
        return material;
    }

    private static ArrayMesh SkiLoft(SkiSection[] sections)
    {
        const int sides = 8;
        var vertices = new Vector3[sections.Length][];
        for (int i = 0; i < sections.Length; i++)
        {
            var (z, wide, bottom, top) = sections[i];
            float sideHeight = Mathf.Lerp(bottom, top, .70f), chine = Mathf.Lerp(bottom, top, .37f);
            vertices[i] =
            [
                new Vector3(0, bottom, z), new Vector3(wide * .60f, chine, z), new Vector3(wide, sideHeight, z),
                new Vector3(wide * .80f, top, z), new Vector3(0, top + .034f, z),
                new Vector3(-wide * .80f, top, z), new Vector3(-wide, sideHeight, z), new Vector3(-wide * .60f, chine, z)
            ];
        }

        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < sections.Length - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            int next = (j + 1) % sides;
            var a = vertices[i][j]; var b = vertices[i][next];
            var c = vertices[i + 1][j]; var d = vertices[i + 1][next];
            var aUv = UV(i, j); var bUv = UV(i, j + 1);
            var cUv = UV(i + 1, j); var dUv = UV(i + 1, j + 1);
            Triangle(a, c, b, aUv, cUv, bUv);
            Triangle(b, c, d, bUv, cUv, dUv);
        }
        for (int j = 1; j < sides - 1; j++)
        {
            Triangle(vertices[0][0], vertices[0][j], vertices[0][j + 1], new Vector2(.37f, .51f), new Vector2(.43f, .50f), new Vector2(.45f, .54f));
            Triangle(vertices[^1][0], vertices[^1][j + 1], vertices[^1][j], new Vector2(.37f, .51f), new Vector2(.45f, .54f), new Vector2(.43f, .50f));
        }
        mesh.GenerateTangents();
        return mesh.Commit();

        Vector2 UV(int ring, int side)
        {
            // Map the broad riveted plate to the hull, leaving the eyes and small parts in the atlas.
            float length = (sections[ring].Z - sections[0].Z) / (sections[^1].Z - sections[0].Z);
            return new Vector2(.115f + Math.Min(side, 8 - side) * .042f, .063f + length * .257f);
        }

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 u, Vector2 v, Vector2 w)
        {
            var normal = (c - a).Cross(b - a).Normalized();
            Add(a, u); Add(b, v); Add(c, w);

            void Add(Vector3 pos, Vector2 uv)
            {
                mesh.SetNormal(normal);
                mesh.SetUV(uv);
                mesh.AddVertex(pos);
            }
        }
    }
}
