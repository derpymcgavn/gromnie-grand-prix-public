using DerethSurvivors;
using Godot;

internal sealed class RaceDriftTrail
{
    // Fire Bolt's original smoke and additive flame sprites in the retail portal DAT.
    private const uint SmokeSprite = 0x060038af, FireSprite = 0x060037cc;
    private static readonly Color FlameTint = new("fff1d2"), SmokeTint = new("ded9d0");
    private readonly RandomNumberGenerator random = new() { Seed = 43192 };
    private readonly Dictionary<MountedRacer, Footpath> paths = [];
    private readonly ParticleBatch smoke, fire;
    private float clock;

    private readonly record struct Footpath(Vector3 Center, Vector3 Span, float At, float Carry, bool Fire, bool OffRoad);
    private struct Particle
    {
        public Vector3 Origin, Velocity;
        public Color Tint;
        public float Age, Life, Width, Height;
    }

    public int SmokeCount => smoke.Count;
    public int FireCount => fire.Count;
    public bool UsesNativeSprites { get; }

    public RaceDriftTrail(Node3D root, AcAssets assets, Texture2D fallback)
    {
        Texture2D? ash = NativeSprite(SmokeSprite);
        Texture2D? flame = NativeSprite(FireSprite);
        UsesNativeSprites = ash is not null && flame is not null;
        smoke = new ParticleBatch(root, "DAT drift smoke", ash ?? fallback, 224, fiery: false);
        fire = new ParticleBatch(root, "DAT super drift fire", flame ?? fallback, 288, fiery: true);

        Texture2D? NativeSprite(uint id) => assets.Archive.Entries.ContainsKey(id) ? assets.Texture(id) : null;
    }

    public void Stop(MountedRacer racer) => paths.Remove(racer);

    public void Clear()
    {
        clock = 0;
        paths.Clear();
        smoke.Clear();
        fire.Clear();
    }

    public void Sample(MountedRacer racer, BogCircuit track, Vector3 center, Vector3 span, bool fiery)
    {
        Vector3 heading = racer.Heading(track);
        center -= heading * .7f;
        bool hadPath = paths.TryGetValue(racer, out Footpath old);
        Vector3 travel = hadPath ? center - old.Center : Vector3.Zero;
        float length = travel.Length();
        float spacing = fiery ? 2.15f : 2.95f;
        float carry = 0;
        if (!hadPath || clock - old.At > .16f || length > 18 || old.Fire != fiery || old.OffRoad != racer.FreeDriving)
            EmitPair(track, center, span, heading, racer.FreeDriving, fiery);
        else if (length > .001f)
        {
            float next = spacing - old.Carry;
            for (; next <= length; next += spacing)
            {
                float along = next / length;
                EmitPair(track, old.Center.Lerp(center, along), old.Span.Lerp(span, along), heading, racer.FreeDriving, fiery);
            }
            carry = length + spacing - next;
        }
        else carry = old.Carry;
        paths[racer] = new Footpath(center, span, clock, carry, fiery, racer.FreeDriving);
    }

    private void EmitPair(BogCircuit track, Vector3 center, Vector3 span, Vector3 heading, bool offRoad, bool fiery)
    {
        Emit(center - span);
        Emit(center + span);

        void Emit(Vector3 point)
        {
            if (offRoad)
            {
                point = track.FreeGround(point);
                if (!track.DustSurface(point, true, out _)) return;
            }
            else point.Y = track.GroundOrRoadHeight(point);

            float width = fiery ? random.RandfRange(1.9f, 2.9f) : random.RandfRange(1.6f, 2.5f);
            float height = width * (fiery ? .88f : .69f);
            var velocity = -heading * random.RandfRange(.4f, fiery ? 1.75f : 3.2f)
                + Vector3.Up * random.RandfRange(.45f, fiery ? 1.4f : 2.6f);
            var puff = new Particle
            {
                Origin = point + Vector3.Up * (height * .37f), Velocity = velocity, Tint = fiery ? FlameTint : SmokeTint,
                Life = fiery ? random.RandfRange(.36f, .59f) : random.RandfRange(.50f, .72f),
                Width = width, Height = height
            };
            (fiery ? fire : smoke).Emit(puff);
        }
    }

    public void Tick(float dt)
    {
        clock += dt;
        smoke.Tick(dt);
        fire.Tick(dt);
    }

    private sealed class ParticleBatch
    {
        // Twelve floats for the 3D transform, followed by RGBA. One bulk upload per effect.
        private const int InstanceStride = 16;
        private readonly Particle[] particles;
        private readonly float[] buffer;
        private readonly MultiMesh mesh;
        private readonly bool fiery;
        public int Count { get; private set; }

        public ParticleBatch(Node3D root, string name, Texture2D sprite, int capacity, bool fiery)
        {
            particles = new Particle[capacity];
            buffer = new float[capacity * InstanceStride];
            this.fiery = fiery;
            var material = new StandardMaterial3D
            {
                AlbedoTexture = sprite, VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = fiery ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled, TextureRepeat = false
            };
            mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                Mesh = new QuadMesh { Size = Vector2.One, Material = material }, InstanceCount = capacity,
                VisibleInstanceCount = 0, CustomAabb = new Aabb(new(-2200, -700, -2200), new(4400, 1400, 4400))
            };
            root.AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }

        public void Emit(Particle particle)
        {
            if (Count < particles.Length) particles[Count++] = particle;
        }

        public void Clear()
        {
            Count = 0;
            mesh.VisibleInstanceCount = 0;
        }

        public void Tick(float dt)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                ref Particle particle = ref particles[i];
                particle.Age += dt;
                if (particle.Age >= particle.Life)
                    particle = particles[--Count];
            }
            for (int i = 0; i < Count; i++)
            {
                ref Particle p = ref particles[i];
                float age = p.Age / p.Life;
                float spread = fiery ? 1 + age * .45f : .82f + age * 1.47f;
                Vector3 origin = p.Origin + p.Velocity * p.Age;
                int slot = i * InstanceStride;
                buffer[slot] = p.Width * spread;
                buffer[slot + 3] = origin.X;
                buffer[slot + 5] = p.Height * spread;
                buffer[slot + 7] = origin.Y;
                buffer[slot + 10] = spread;
                buffer[slot + 11] = origin.Z;
                buffer[slot + 12] = p.Tint.R;
                buffer[slot + 13] = p.Tint.G;
                buffer[slot + 14] = p.Tint.B;
                buffer[slot + 15] = (fiery ? .94f : .73f) * (1 - Mathf.SmoothStep(.15f, 1, age));
            }
            if (Count > 0) mesh.Buffer = buffer;
            mesh.VisibleInstanceCount = Count;
        }
    }
}
