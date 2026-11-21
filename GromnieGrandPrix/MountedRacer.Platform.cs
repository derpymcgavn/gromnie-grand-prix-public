using Godot;
using DerethSurvivors;

public sealed partial class MountedRacer
{
    private const uint CrossLegged = 0x4300013e, Meditate = 0x4300011c;
    // Crimson Storm (spell 6151) uses this mesh in projectile emitter 0x320004cc.
    private const uint CrimsonStormMesh = 0x01001cb9;
    private CpuParticles3D? discStorm;
    private float hoverTime;

    private void FitPlatform(AcAssets assets)
    {
        bool carpet = Rig.Platform == MountPlatform.Carpet;
        // The rug is already horizontal. The shield's convex face points down under the rider.
        var basis = carpet ? Basis.FromScale(new(.73f, 1, 1.04f)) :
            new Basis(Vector3.Right, Mathf.Pi * .5f).Scaled(Vector3.One * 2.5f);
        Aabb bounds = BodyBounds(Mount, basis);
        Vector3 center = bounds.GetCenter();
        var deck = new Transform3D(basis, new(-center.X, -bounds.End.Y, -center.Z));
        for (int i = 0; i < Mount.Setup.Parts.Length; i++)
            Mount.Part(i).Transform = deck * Mount.Part(i).Transform;
        Mount.RefreshBodyPoseCache();

        if (carpet) return;
        AddDiscStorm(assets, bounds.Size);
    }

    private void AddDiscStorm(AcAssets assets, Vector3 shieldSize)
    {
        if (!assets.Archive.Entries.ContainsKey(CrimsonStormMesh)) return;
        var mesh = assets.Mesh(CrimsonStormMesh, particle: true);
        if (mesh.GetSurfaceCount() == 0 || mesh.SurfaceGetMaterial(0) is not StandardMaterial3D nativeMaterial) return;
        var material = (StandardMaterial3D)nativeMaterial.Duplicate();
        material.AlbedoColor *= new Color(1, .52f, .63f);
        material.VertexColorUseAsAlbedo = true;
        material.VertexColorIsSrgb = true;

        var points = new Vector3[8];
        var normals = new Vector3[points.Length];
        float radius = Math.Clamp(Math.Min(shieldSize.X, shieldSize.Z) * .19f, .22f, .46f);
        for (int i = 0; i < points.Length; i++)
        {
            var outward = Vector3.Right.Rotated(Vector3.Up, Mathf.Tau * i / points.Length);
            points[i] = outward * radius;
            normals[i] = (outward + Vector3.Down * .23f).Normalized();
        }

        discStorm = new CpuParticles3D
        {
            Name = "Crimson Storm",
            Position = Vector3.Down * (shieldSize.Y + .025f),
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            LocalCoords = true,
            Amount = 22, Lifetime = .31f, Preprocess = .31f, LifetimeRandomness = .26f, FixedFps = 30,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.DirectedPoints,
            EmissionPoints = points, EmissionNormals = normals,
            Direction = Vector3.Back, Spread = 22, Gravity = Vector3.Zero,
            InitialVelocityMin = 2, InitialVelocityMax = 4.1f,
            ScaleAmountMin = .19f, ScaleAmountMax = .32f,
            ParticleFlagRotateY = true, AngleMin = -180, AngleMax = 180,
            ColorRamp = new Gradient
            {
                Offsets = [0, .14f, .62f, 1],
                Colors = [new Color(1, 1, 1, .05f), new Color(1, 1, 1, .88f),
                    new Color(1, 1, 1, .46f), new Color(1, 1, 1, 0)]
            },
            VisibilityRangeEnd = 130, VisibilityRangeEndMargin = 15,
            VisibilityAabb = new Aabb(Vector3.One * -3, Vector3.One * 6)
        };
        Mount.AddChild(discStorm);
    }

    private void PosePlatformRider()
    {
        Rider.HoldFinalPose = false;
        if (Rig.Platform == MountPlatform.Carpet)
        {
            if (Rider.Play(CrossLegged, false) || Rider.Play(Meditate, false)) Rider.TickCurrentMotion(0);
            else PoseSeated(relativeScale, Rig.LegSpread, legPitch: -.40f);
            FoldCarpetLeg(1, 25, -1);
            FoldCarpetLeg(5, 26, 1);
        }
        else
        {
            Rider.Play(0x41000003, false);
            Rider.TickCurrentMotion(0);
            SplayLeg(1, [1, 2, 3, 4, 25], -1, Rig.LegSpread, 0);
            SplayLeg(5, [5, 6, 7, 8, 26], 1, Rig.LegSpread, 0);
        }

        Rider.Rotation = Vector3.Zero;
        Aabb bounds = BodyBounds(Rider, Basis.Identity);
        Vector3 center = bounds.GetCenter();
        Rider.Position = new Vector3(-center.X, -bounds.Position.Y, -center.Z) * riderScale + Vector3.Up * .018f;
        Rider.RefreshBodyPoseCache();
    }

    private void FoldCarpetLeg(int hipPart, int armorPart, int side)
    {
        var thigh = Rider.Part(hipPart);
        var shin = Rider.Part(hipPart + 1);
        Vector3 knee = shin.Position;
        Vector3 thighBone = knee - thigh.Position;
        Vector3 shinBone = Rider.Part(hipPart + 2).Position - knee;
        if (thighBone.LengthSquared() < .0001f || shinBone.LengthSquared() < .0001f) return;

        // Fold both legs onto the deck. The native emote leaves one boot planted below the seat.
        Vector3 thighDirection = new Vector3(side * .57f, -.12f, -.81f).Normalized();
        Vector3 shinDirection = new Vector3(-side * .92f, side * .09f, .37f).Normalized();
        Vector3 foldedKnee = thigh.Position + thighDirection * thighBone.Length();
        var thighTurn = new Basis(new Quaternion(thighBone.Normalized(), thighDirection).Normalized());
        var shinTurn = new Basis(new Quaternion(shinBone.Normalized(), shinDirection).Normalized());
        thigh.Basis = thighTurn * thigh.Basis;
        shin.Transform = new Transform3D(shinTurn * shin.Basis, foldedKnee);
        FoldFoot(hipPart + 2);
        FoldFoot(hipPart + 3);
        if (armorPart < Rider.Setup.Parts.Length) FoldFoot(armorPart);

        void FoldFoot(int index)
        {
            var foot = Rider.Part(index);
            foot.Transform = new Transform3D(shinTurn * foot.Basis, foldedKnee + shinTurn * (foot.Position - knee));
        }
    }

    private void DrawPlatform(float dt, float steering)
    {
        bool carpet = Rig.Platform == MountPlatform.Carpet;
        hoverTime = (hoverTime + dt * (carpet ? 3.5f : 4.4f)) % Mathf.Tau;
        float floatHeight = carpet ? .73f : .63f;
        Mount.Position = Vector3.Up * (floatHeight + Mathf.Sin(hoverTime) * .065f + (Boost > 0 ? .12f : 0));
        Mount.Rotation = new(Mathf.Cos(hoverTime) * .023f - (Boost > 0 ? .045f : 0),
            -steering * .17f, -Lean * (carpet ? .30f : .43f) + Mathf.Sin(hoverTime) * .018f);
        if (discStorm is not null) discStorm.SpeedScale = Boost > 0 ? 1.35f : 1;
    }

    private static Aabb BodyBounds(AcActor actor, Basis orientation)
    {
        Aabb? bounds = null;
        var transform = new Transform3D(orientation, Vector3.Zero);
        foreach (var (mesh, part) in actor.BodyMeshBindings)
        {
            Aabb piece = transform * actor.Part(part).Transform * mesh.Transform * mesh.Mesh.GetAabb();
            bounds = bounds?.Merge(piece) ?? piece;
        }
        return bounds ?? throw new InvalidDataException($"Native setup {actor.Setup.Id:X8} has no visible parts.");
    }
}
