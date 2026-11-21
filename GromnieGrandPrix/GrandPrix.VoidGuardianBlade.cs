using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    // The native sword runs from the pommel at -1.03 m to the point at 2.99 m along Part 0's +Y axis.
    private const float VoidGuardianSwordLength = 4.0255f;
    private const float VoidBladeBase = .28f, VoidBladePoint = 2.94f;

    private sealed class VoidGuardianBlade(AcActor guardian, int grip, AcActor sword,
        CpuParticles3D flames, CpuParticles3D sparks)
    {
        public readonly AcActor Guardian = guardian, Sword = sword;
        public readonly CpuParticles3D Flames = flames, Sparks = sparks;
        public Vector3 Tip { get; private set; }

        public void Draw(bool charged, bool reset)
        {
            var blade = Guardian.Transform * Guardian.BodyPartTransform(grip) * Sword.Transform * Sword.BodyPartTransform(0);
            Vector3 start = blade * (Vector3.Up * VoidBladeBase);
            Tip = blade * (Vector3.Up * VoidBladePoint);
            Vector3 along = (Tip - start).Normalized();
            Vector3 across = along.Cross(Math.Abs(along.Dot(Vector3.Up)) > .93f ? Vector3.Right : Vector3.Up).Normalized();
            var basis = new Basis(across, along, across.Cross(along));
            Flames.Transform = new Transform3D(basis, start.Lerp(Tip, .5f));
            Sparks.Transform = new Transform3D(basis, start.Lerp(Tip, .93f));
            if (reset)
            {
                Flames.Restart();
                Sparks.Restart();
            }
            if (reset || Sparks.Emitting != charged) Sparks.Emitting = charged;
        }
    }

    private static VoidGuardianBlade BuildVoidGuardianBlade(Node3D stage, AcActor guardian, int grip,
        AcActor sword, Mesh ember, int side)
    {
        bool west = side == 0;
        string suffix = west ? "West" : "East";
        var swordPose = sword.Transform * sword.BodyPartTransform(0);
        float bladeHalf = (VoidBladePoint - VoidBladeBase) * guardian.Scale.Y * swordPose.Basis.Y.Length() * .5f;
        var fire = new Color(west ? "ffab57" : "66ebff");
        var emberColor = new Color(west ? "fc6035" : "5289ff");
        var white = new Color(west ? "fff1b4" : "e5fcff");
        var bladeGlow = new StandardMaterial3D
        {
            AlbedoColor = new Color(fire.Lightened(.30f), .47f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add
        };
        var core = BogCircuit.MeshAt(sword.Part(0), new CylinderMesh
        {
            BottomRadius = .083f, TopRadius = .015f, Height = VoidBladePoint - VoidBladeBase,
            RadialSegments = 6, Rings = 0
        }, Vector3.Up * ((VoidBladeBase + VoidBladePoint) * .5f), Vector3.One, bladeGlow);
        core.Name = "Blade fire";
        core.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var flames = new CpuParticles3D
        {
            Name = $"Blade embers {suffix}", Mesh = ember, Emitting = true,
            Amount = 44, Lifetime = .85f, LifetimeRandomness = .27f, Preprocess = .65f,
            LocalCoords = false, FixedFps = 30, Randomness = .35f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(.64f, bladeHalf, .64f),
            Direction = Vector3.Up, Spread = 72, Gravity = Vector3.Up * 5,
            InitialVelocityMax = 9, InitialVelocityMin = 3, ScaleAmountMax = 1.55f, ScaleAmountMin = .48f,
            ColorRamp = TrailRamp(white, fire, emberColor, .83f),
            VisibilityRangeEnd = 1450, VisibilityRangeEndMargin = 70,
            VisibilityAabb = new Aabb(Vector3.One * -70, Vector3.One * 140),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        var sparks = new CpuParticles3D
        {
            Name = $"Blade sparks {suffix}", Mesh = ember, Emitting = false,
            Amount = 36, Lifetime = .68f, LifetimeRandomness = .21f, Preprocess = .27f,
            LocalCoords = false, FixedFps = 30, Randomness = .45f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.25f,
            Direction = Vector3.Up, Spread = 56, Gravity = Vector3.Down * 6,
            InitialVelocityMax = 25, InitialVelocityMin = 9, ScaleAmountMax = 1.19f, ScaleAmountMin = .44f,
            ColorRamp = TrailRamp(white, fire.Lightened(.2f), fire, .96f),
            VisibilityRangeEnd = 1450, VisibilityRangeEndMargin = 70,
            VisibilityAabb = new Aabb(Vector3.One * -70, Vector3.One * 140),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        stage.AddChild(flames);
        stage.AddChild(sparks);
        return new VoidGuardianBlade(guardian, grip, sword, flames, sparks);

        static Gradient TrailRamp(Color hot, Color lit, Color fading, float opacity) => new()
        {
            Offsets = [0, .12f, .63f, 1],
            Colors = [new Color(hot, .2f * opacity), new Color(hot, opacity), new Color(lit, .52f * opacity), new Color(fading, 0)]
        };
    }

    private static QuadMesh BuildVoidGuardianEmber()
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 p = new Vector2(x + .5f, y + .5f) * (2f / size) - Vector2.One;
            float radius = p.LengthSquared();
            float halo = MathF.Exp(-radius * 5.2f);
            float core = MathF.Exp(-radius * 21);
            float alpha = Math.Min(1, halo * .65f + core) * (1 - Mathf.SmoothStep(.48f, 1, radius));
            int pixel = (y * size + x) * 4;
            pixels[pixel] = pixels[pixel + 1] = pixels[pixel + 2] = 255;
            pixels[pixel + 3] = (byte)Mathf.RoundToInt(alpha * 255);
        }
        using var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, pixels);
        image.GenerateMipmaps();
        var material = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image), VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true
        };
        return new QuadMesh { Size = new Vector2(3.3f, 5.0f), Material = material };
    }
}
