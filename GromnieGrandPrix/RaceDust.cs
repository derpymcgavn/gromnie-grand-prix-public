using Godot;
using DerethSurvivors;

internal sealed class RaceDust
{
    private const int Capacity = 144;
    private static readonly Color Dirt = new("ba9a6a"), Grass = new("a8a178"), Stone = new("bbb4a2"), VoidSpark = new("eb87eb");
    private readonly RandomNumberGenerator random = new() { Seed = 6421 };
    private readonly Puff[] puffs = new Puff[Capacity];
    private readonly MultiMesh mesh;
    private readonly RaceDriftTrail skidTrail;
    private int count;

    private struct Puff
    {
        public Vector3 Origin, Velocity;
        public Color Color;
        public float Age, Lifetime, Size, Opacity;
    }

    public int Count => count;
    public int DriftSmokeCount => skidTrail.SmokeCount;
    public int SuperDriftFireCount => skidTrail.FireCount;
    public bool UsesNativeDriftSprites => skidTrail.UsesNativeSprites;

    public RaceDust(Node3D root, AcAssets assets)
    {
        var dustSprite = MakeDustTexture();
        var material = new StandardMaterial3D
        {
            AlbedoTexture = dustSprite, VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = material }, InstanceCount = Capacity,
            VisibleInstanceCount = 0, CustomAabb = new Aabb(new(-2000, -700, -2000), new(4000, 1400, 4000))
        };
        root.AddChild(new MultiMeshInstance3D { Name = "Footstep dust", Multimesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        skidTrail = new RaceDriftTrail(root, assets, dustSprite);
    }

    public void Clear()
    {
        count = 0;
        mesh.VisibleInstanceCount = 0;
        skidTrail.Clear();
    }

    public void SampleFeet(MountedRacer racer, BogCircuit track, bool drifting, bool superDrift = false)
    {
        if (racer.MountUnavailable)
        {
            skidTrail.Stop(racer);
            return;
        }
        bool skid = (drifting || superDrift) && racer.Speed > 7;
        Vector3 origin = skid ? racer.CoursePosition(track) : Vector3.Zero;
        Vector3 right = skid ? racer.Right(track).Normalized() : Vector3.Zero;
        float footSpread = .46f;
        for (int foot = 0; foot < racer.DustContact.Length; foot++)
        {
            Vector3 claw = racer.FootPosition(foot);
            if (skid) footSpread = Math.Max(footSpread, Math.Abs((claw - origin).Dot(right)));
            Vector3 ground = track.FreeGround(claw);
            if (!racer.FreeDriving)
                ground.Y = track.RoadHeight(claw, ground.Y);
            float height = claw.Y - ground.Y;
            bool wasPlanted = racer.DustContact[foot];
            bool planted = height < (wasPlanted ? .19f : .095f);
            racer.DustContact[foot] = planted;
            if (!planted || wasPlanted || racer.Speed < 3.8f || !track.DustSurface(ground, racer.FreeDriving, out bool stone)) continue;

            float strength = Mathf.Clamp(Mathf.SmoothStep(3.8f, 24, racer.Speed) + (racer.Boost > 0 ? .18f : 0), 0, 1);
            Color tint = track.IsVoid ? VoidSpark : stone ? Stone : racer.FreeDriving ? Grass : Dirt;
            Vector3 heading = racer.Heading(track);
            Emit(ground, heading, tint, strength);
            if (strength > .49f) Emit(ground, heading, tint, strength);
        }
        if (skid) skidTrail.Sample(racer, track, origin, right * Math.Min(footSpread, 1.3f), superDrift);
        else skidTrail.Stop(racer);
    }

    public void StoneImpact(Vector3 at, Vector3 direction, float strength)
    {
        for (int puff = 0; puff < 6; puff++)
            Emit(at, direction.Rotated(Vector3.Up, puff * Mathf.Tau / 6), Stone, strength);
    }

    private void Emit(Vector3 ground, Vector3 heading, Color tint, float strength)
    {
        if (count == Capacity) return;
        Vector3 scatter = new(random.RandfRange(-.17f, .17f), 0, random.RandfRange(-.17f, .17f));
        puffs[count++] = new Puff
        {
            Origin = ground + Vector3.Up * .09f + scatter,
            Velocity = -heading * (random.RandfRange(.7f, 1.55f) + strength * 1.65f)
                + Vector3.Up * random.RandfRange(.22f, .55f) + scatter * 2.4f,
            Lifetime = random.RandfRange(.42f, .68f), Size = random.RandfRange(.38f, .55f) * (.68f + strength * .52f),
            Color = tint.Lerp(new Color("e1c69b"), random.RandfRange(0, .26f)),
            Opacity = .27f + strength * .32f
        };
    }

    public void Tick(float dt)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            ref Puff p = ref puffs[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime)
            {
                p = puffs[--count];
                continue;
            }
        }
        for (int i = 0; i < count; i++)
        {
            ref Puff p = ref puffs[i];
            float t = p.Age / p.Lifetime;
            float size = p.Size * (1 + 1.9f * Mathf.Sqrt(t));
            var basis = Basis.Identity.Scaled(new Vector3(size, size * .64f, size));
            mesh.SetInstanceTransform(i, new Transform3D(basis, p.Origin + p.Velocity * p.Age + Vector3.Up * (size * .2f)));
            float alpha = p.Opacity * Mathf.SmoothStep(0, .11f, t) * (1 - Mathf.SmoothStep(.14f, 1, t));
            mesh.SetInstanceColor(i, new Color(p.Color, alpha));
        }
        mesh.VisibleInstanceCount = count;
        skidTrail.Tick(dt);
    }

    private static Texture2D MakeDustTexture()
    {
        const int size = 48;
        var noise = new FastNoiseLite { Seed = 552, Frequency = .116f, FractalOctaves = 3 };
        var bytes = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + .5f, y + .5f) * (2f / size) - Vector2.One;
                float cloud = (1 - Mathf.SmoothStep(.10f, .94f, p.Length() + noise.GetNoise2D(x, y) * .22f));
                int index = (y * size + x) * 4;
                bytes[index] = bytes[index + 1] = bytes[index + 2] = 255;
                bytes[index + 3] = (byte)Mathf.RoundToInt(255 * cloud * cloud);
            }
        using var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, bytes);
        return ImageTexture.CreateFromImage(image);
    }
}
