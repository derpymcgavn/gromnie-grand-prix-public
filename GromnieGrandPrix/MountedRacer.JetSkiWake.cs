using Godot;
using DerethSurvivors;

public sealed partial class MountedRacer
{
    private const uint RegattaSpraySprite = 0x060038af, RegattaWakeTexture = 0x0500146a;

    private static ShaderMaterial CreateJetSkiWakeMaterial(AcAssets assets)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RegattaWake.gdshader") };
        RegattaSurface.Configure(material);
        if (assets.Archive.Entries.ContainsKey(RegattaWakeTexture) && assets.Texture(RegattaWakeTexture) is { } water)
            material.SetShaderParameter("native_running_water", water);
        return material;
    }

    internal static ShaderMaterial CreateRegattaSprayMaterial(Texture2D? sprite)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/RegattaSpray.gdshader") };
        if (sprite is not null) material.SetShaderParameter("native_sprite", sprite);
        return material;
    }

    private sealed class JetSkiWake
    {
        private const int Capacity = 64, RibbonBands = 6;
        private const float SampleInterval = 1f / 28, Life = 2.18f, BreakDistance = 16;
        private static readonly Vector3 JetOutlet = new(0, .062f, 1.92f);
        private static readonly Vector3 PortChine = new(-.58f, .050f, -.24f);
        private static readonly Vector3 StarboardChine = new(.58f, .050f, -.24f);
        private static readonly Gradient DropletRamp = new()
        {
            Offsets = [0f, .12f, .38f, .72f, 1f],
            Colors = [new Color(.82f, .94f, .95f, .1f), new Color(.91f, .98f, 1, .86f),
                new Color(.88f, .97f, .98f, .74f), new Color(.71f, .86f, .90f, .24f), new Color(.68f, .81f, .86f, 0)]
        };
        private static readonly QuadMesh Droplet = new() { Size = new Vector2(.39f, .31f) };

        private readonly record struct Station(Vector3 Position, Vector3 Right, float Born, float Thrust,
            float Cut, float Contact, bool Boosted, bool StartsSegment);
        private readonly record struct CrossSection(Vector3 Center, Vector3 HalfWidth, Color Color, float Age);
        private readonly Station[] stations = new Station[Capacity];
        private readonly CrossSection[] sections = new CrossSection[Capacity];
        private readonly ImmediateMesh strip = new();
        private readonly MeshInstance3D ribbon;
        private readonly ShaderMaterial foam;
        private readonly CpuParticles3D jet, port, starboard;
        private int first, count;
        private float clock, nextSample;
        private Vector3 lastOrigin;
        private bool hasOrigin, joinedWake;

        public JetSkiWake(Node3D root, ShaderMaterial foam, Material spray)
        {
            this.foam = foam;
            ribbon = new MeshInstance3D
            {
                Name = "Ski's two spreading foam lines", Mesh = strip, TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = 190, VisibilityRangeEndMargin = 20
            };
            root.AddChild(ribbon);
            jet = MakeSpray("Pump wash", 44, .56f, .22f, 1.25f);
            port = MakeSpray("Port chine spray", 30, .48f, .18f, .98f);
            starboard = MakeSpray("Starboard chine spray", 30, .48f, .18f, .98f);

            CpuParticles3D MakeSpray(string name, int amount, float lifespan, float minScale, float maxScale)
            {
                var particles = new CpuParticles3D
                {
                    Name = name, TopLevel = true, Emitting = false, LocalCoords = false,
                    Mesh = Droplet, MaterialOverride = spray, ColorRamp = DropletRamp,
                    Amount = amount, Lifetime = lifespan, LifetimeRandomness = .27f, FixedFps = 28,
                    Gravity = Vector3.Down * 17, ScaleAmountMin = minScale, ScaleAmountMax = maxScale,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    VisibilityRangeEnd = 145, VisibilityRangeEndMargin = 12,
                    VisibilityAabb = new Aabb(new Vector3(-45, -5, -45), new Vector3(90, 26, 90))
                };
                root.AddChild(particles);
                return particles;
            }
        }

        public void Update(float dt, Transform3D pose, float waterLevel, float waterTime, float speed, float steering,
            float lean, bool drifting, bool boosted, float contact, RegattaFluidField? fluid)
        {
            clock += dt;
            foam.SetShaderParameter("regatta_time", waterTime);
            Vector3 origin = pose.Origin;
            Vector3 displacement = hasOrigin ? origin - lastOrigin : Vector3.Zero;
            displacement.Y = 0;
            hasOrigin = true;
            lastOrigin = origin;

            if (displacement.LengthSquared() > BreakDistance * BreakDistance)
            {
                // Checkpoint recalls must not connect old and new positions across the bay.
                first = count = 0;
                joinedWake = false;
                strip.ClearSurfaces();
                jet.Restart();
                port.Restart();
                starboard.Restart();
            }
            bool onWater = contact > .08f && speed > 2.3f;
            float portContact = 0, starboardContact = 0;
            if (!onWater)
            {
                joinedWake = false;
                jet.Emitting = port.Emitting = starboard.Emitting = false;
            }
            else
            {
                Vector3 forward = new Vector3(-pose.Basis.Z.X, 0, -pose.Basis.Z.Z).Normalized();
                Basis yaw = Basis.LookingAt(forward, Vector3.Up);
                PlaceSpray(jet, JetOutlet);
                portContact = PlaceSpray(port, PortChine);
                starboardContact = PlaceSpray(starboard, StarboardChine);

                float PlaceSpray(CpuParticles3D spray, Vector3 source)
                {
                    Vector3 position = pose * source;
                    float surface = waterLevel + RegattaSurface.HeightOffset(new Vector2(position.X, position.Z), waterTime, fluid);
                    float chine = (pose * (source with { Y = -.18f })).Y;
                    position.Y = surface + source.Y;
                    spray.GlobalTransform = new Transform3D(yaw, position);
                    return Mathf.SmoothStep(-.025f, .105f, surface - chine);
                }
            }
            if (clock < nextSample) return;
            nextSample = Math.Max(clock, nextSample + SampleInterval);

            float thrust = Mathf.SmoothStep(2.3f, 52, speed);
            float turn = Mathf.Clamp(steering * .83f + lean * .77f, -1, 1);
            if (onWater) UpdateSpray(contact, portContact, starboardContact, thrust, turn, drifting, boosted);

            while (count > 0 && clock - stations[first].Born >= Life)
            {
                first = (first + 1) % Capacity;
                count--;
            }

            if (onWater)
            {
                Vector3 stern = pose * new Vector3(0, 0, 1.88f);
                stern.Y = waterLevel + .067f;
                if (displacement.LengthSquared() < .001f)
                    displacement = new Vector3(-pose.Basis.Z.X, 0, -pose.Basis.Z.Z);
                Vector3 right = displacement.Normalized().Cross(Vector3.Up);
                if (!joinedWake || count == 0 || stations[(first + count - 1) % Capacity].Position.DistanceSquaredTo(stern) > .09f)
                {
                    if (count == Capacity)
                    {
                        first = (first + 1) % Capacity;
                        count--;
                    }
                    stations[(first + count) % Capacity] = new(stern, right, clock, thrust, turn, contact, boosted, !joinedWake);
                    count++;
                    joinedWake = true;
                }
            }
            DrawRibbon(origin);
        }

        private void UpdateSpray(float contact, float portContact, float starboardContact, float thrust,
            float turn, bool drifting, bool boosted)
        {
            jet.Emitting = true;
            float jetPressure = Mathf.Lerp(.48f, 1, contact);
            jet.Direction = new Vector3(-turn * .32f, .25f + (boosted ? .16f : 0), 1).Normalized();
            jet.InitialVelocityMin = (3.4f + thrust * 5.4f + (boosted ? 3.2f : 0)) * jetPressure;
            jet.InitialVelocityMax = (7 + thrust * 13 + (boosted ? 8 : 0)) * jetPressure;
            jet.Spread = boosted ? 19 : 10;
            jet.SpeedScale = .72f + .64f * thrust;
            Side(port, -1, portContact);
            Side(starboard, 1, starboardContact);

            void Side(CpuParticles3D spray, int sign, float chineContact)
            {
                float cut = Mathf.Clamp(-turn * sign * 1.08f + (drifting ? .38f : 0), 0, 1);
                float pressure = Mathf.Lerp(.35f, 1, chineContact);
                spray.Emitting = chineContact > .10f && (thrust > .36f || cut > .19f);
                spray.Direction = new Vector3(sign * (.68f + .59f * cut), .26f + .31f * cut, .76f).Normalized();
                spray.InitialVelocityMin = (2.5f + thrust * 4.3f + cut * 3.3f) * pressure;
                spray.InitialVelocityMax = (5.5f + thrust * 6.5f + cut * 10.5f) * pressure;
                spray.Spread = 12 + 13 * cut;
                spray.SpeedScale = .71f + .42f * thrust + .24f * cut;
            }
        }

        private void DrawRibbon(Vector3 origin)
        {
            strip.ClearSurfaces();
            if (count < 2) return;
            ribbon.GlobalPosition = origin;
            for (int i = 0; i < count; i++)
            {
                var station = stations[(first + i) % Capacity];
                float age = clock - station.Born;
                float width = .91f + age * (2.6f + station.Thrust * 3.0f + (station.Boosted ? .8f : 0));
                Vector3 midpoint = station.Position - origin - station.Right * (station.Cut * age * 1.44f);
                Color tint = new(station.Thrust, .5f + .5f * station.Cut, station.Boosted ? 1 : 0, station.Contact);
                sections[i] = new(midpoint, station.Right * width, tint, age / Life);
            }

            // Tessellate across the widening trail so its foam follows the displaced surface.
            // Degenerate vertices join the bands into one draw and leave hops and gaps open.
            int previous = -1;
            float previousRight = 0;
            for (int band = 0; band < RibbonBands; band++)
            {
                float left = (float)band / RibbonBands;
                float right = (float)(band + 1) / RibbonBands;
                int start = 0;
                while (start < count)
                {
                    int end = start + 1;
                    while (end < count && !stations[(first + end) % Capacity].StartsSegment) end++;
                    if (end - start > 1)
                    {
                        if (previous < 0) strip.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, foam);
                        else
                        {
                            AddVertex(previous, previousRight);
                            AddVertex(start, left);
                        }
                        for (int i = start; i < end; i++)
                        {
                            AddVertex(i, left);
                            AddVertex(i, right);
                        }
                        previous = end - 1;
                        previousRight = right;
                    }
                    start = end;
                }
            }
            if (previous >= 0) strip.SurfaceEnd();

            void AddVertex(int index, float across)
            {
                var section = sections[index];
                strip.SurfaceSetColor(section.Color);
                strip.SurfaceSetUV(new Vector2(across, section.Age));
                strip.SurfaceAddVertex(section.Center + section.HalfWidth * (across * 2 - 1));
            }
        }
    }
}
