using Godot;

public partial class GrandPrix
{
    private const int IslandSplashPoolSize = 14;
    private readonly List<IslandSplash> islandSplashes = [];
    private readonly bool[] islandEjected = new bool[RacerCount], islandLanded = new bool[RacerCount];
    private readonly float[] islandShoreSplashCooldown = new float[RacerCount];
    private readonly float[] islandLandingSplashCooldown = new float[RacerCount];
    private readonly WaterFootsteps[] islandFootsteps = new WaterFootsteps[RacerCount];

    private struct WaterFootsteps
    {
        public float SinceStep;
        public bool LeftPlanted, RightPlanted, NextRight;
    }

    private sealed class IslandSplash(Node3D root, MeshInstance3D ring, ShaderMaterial material, CpuParticles3D spray)
    {
        public readonly Node3D Root = root;
        public readonly MeshInstance3D Ring = ring;
        public readonly ShaderMaterial Material = material;
        public readonly CpuParticles3D Spray = spray;
        public float Age, Duration, Radius, Stretch;
    }

    private void BuildIslandSplashes()
    {
        islandSplashes.Clear();
        if (!track.HasWaterSections || DisplayServer.GetName() == "headless") return;

        var root = new Node3D { Name = "Regatta impact ripples and spray" };
        courseItems.AddChild(root);
        var ringMesh = new PlaneMesh { Size = new Vector2(2, 2), SubdivideWidth = 18, SubdivideDepth = 18 };
        var dropMesh = new QuadMesh { Size = new Vector2(.24f, .31f) };
        var sprayMaterial = MountedRacer.CreateRegattaSprayMaterial(assets.Texture(0x060038af));
        var nativeRipples = assets.Texture(0x0500146a);
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, shadows_disabled, fog_disabled;
                #include "res://Shaders/RegattaSurface.gdshaderinc"

                uniform sampler2D native_ripples : source_color, filter_linear_mipmap, repeat_enable;
                uniform vec4 foam_color : source_color = vec4(0.85, 0.96, 1.0, 1.0);
                uniform float progress;
                void vertex() {
                    vec2 water_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
                    VERTEX.y += regatta_wave_height(water_xz) * regatta_geometry_strength(water_xz, CAMERA_POSITION_WORLD.xz);
                }

                void fragment() {
                    vec2 p = UV * 2.0 - 1.0;
                    float radius = length(p);
                    float angle = atan(p.y, p.x);
                    float irregular = 0.025 * sin(angle * 7.0 + 2.4 * progress) +
                        0.014 * cos(angle * 19.0 - 1.8 * progress);
                    float crest = 1.0 - smoothstep(0.012, 0.064, abs(radius - 0.82 + irregular));
                    float inner = 1.0 - smoothstep(0.009, 0.038, abs(radius - 0.62 - irregular));
                    float breakup = 0.44 + 0.56 * smoothstep(-0.62, 0.72,
                        sin(angle * 13.0 + 3.0 * cos(angle * 7.0) + progress * 3.0));
                    vec3 ripple = texture(native_ripples, UV * 2.4 + vec2(progress * 0.28, 0.0)).rgb;
                    float detail = 0.57 + 0.43 * smoothstep(0.10, 0.67, dot(ripple, vec3(0.24, 0.57, 0.19)));
                    ALBEDO = foam_color.rgb * (0.81 + 0.19 * ripple);
                    ALPHA = foam_color.a * (crest * breakup + 0.28 * inner) * detail *
                        smoothstep(0.0, 0.085, progress) * (1.0 - smoothstep(0.23, 1.0, progress));
                }
                """
        };
        var droplets = new Gradient
        {
            Offsets = [0, .17f, .64f, 1],
            Colors = [new Color(.82f, .96f, 1, 0), new Color(.91f, .99f, 1, .93f),
                new Color(.78f, .94f, 1, .69f), new Color(.78f, .94f, 1, 0)]
        };

        for (int i = 0; i < IslandSplashPoolSize; i++)
        {
            var anchor = new Node3D { Name = $"Water disturbance {i + 1}", Visible = false };
            root.AddChild(anchor);
            var material = new ShaderMaterial { Shader = shader, RenderPriority = 3 };
            RegattaSurface.Configure(material);
            if (nativeRipples is not null) material.SetShaderParameter("native_ripples", nativeRipples);
            var ring = BogCircuit.MeshAt(anchor, ringMesh, Vector3.Zero, Vector3.One, material);
            ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            var spray = new CpuParticles3D
            {
                Name = "Short-lived water spray", Mesh = dropMesh, MaterialOverride = sprayMaterial,
                Position = Vector3.Up * .09f, OneShot = true, Emitting = false, Amount = 25, Lifetime = .56f,
                LifetimeRandomness = .28f, Explosiveness = .94f, FixedFps = 30, LocalCoords = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Spread = 55,
                Gravity = Vector3.Down * 16, ColorRamp = droplets,
                ScaleAmountMin = .42f, ScaleAmountMax = 1.4f,
                VisibilityAabb = new Aabb(new Vector3(-10, -1, -10), new Vector3(20, 9, 20))
            };
            anchor.AddChild(spray);
            islandSplashes.Add(new(anchor, ring, material, spray));
        }
    }

    private void ResetIslandSplashes()
    {
        Array.Clear(islandEjected);
        Array.Clear(islandLanded);
        Array.Clear(islandShoreSplashCooldown);
        Array.Clear(islandLandingSplashCooldown);
        Array.Clear(islandFootsteps);
        foreach (var splash in islandSplashes)
        {
            splash.Spray.Emitting = false;
            splash.Root.Hide();
        }
    }

    private void TickIslandSplashes(float dt)
    {
        if (islandSplashes.Count == 0 || track.WaterCourseLevel is not { } waterLevel) return;
        foreach (var splash in islandSplashes)
        {
            if (!splash.Root.Visible) continue;
            splash.Age += dt;
            if (splash.Age >= splash.Duration)
            {
                splash.Spray.Emitting = false;
                splash.Root.Hide();
                continue;
            }
            float progress = splash.Age / splash.Duration;
            float extent = splash.Radius * Mathf.Lerp(.21f, 1, Mathf.Pow(progress, .67f));
            splash.Ring.Scale = new Vector3(extent, 1, extent * splash.Stretch);
            splash.Material.SetShaderParameter("progress", progress);
            splash.Material.SetShaderParameter("regatta_time", RegattaWaterTime);
        }
        for (int i = 0; i < Math.Min(racers.Count, RacerCount); i++)
        {
            var racer = racers[i];
            islandShoreSplashCooldown[i] = Math.Max(0, islandShoreSplashCooldown[i] - dt);
            islandLandingSplashCooldown[i] = Math.Max(0, islandLandingSplashCooldown[i] - dt);
            bool ejected = racer.UsesJetSki && racer.EjectTime > 0 && racer.Rider.TopLevel;
            if (ejected && !islandEjected[i])
            {
                EmitIslandSplash(racer.CoursePosition(track), racer.Heading(track), 2.15f, .77f);
                islandLanded[i] = false;
            }
            else if (ejected && !islandLanded[i])
            {
                Vector3 point = racer.Rider.GlobalPosition;
                if (point.Y <= waterLevel + WaveAt(point, RegattaWaterTime) + .59f)
                {
                    islandLanded[i] = true;
                    if (track.NavigableWater(point))
                        EmitIslandSplash(point, racer.Heading(track), 1.48f, .61f);
                }
            }
            islandEjected[i] = ejected;
            TickIslandFootsteps(racer, ref islandFootsteps[i], waterLevel, dt);
        }
    }

    private void SplashJesusModeStart(MountedRacer racer) =>
        EmitIslandSplash(racer.CoursePosition(track), racer.Heading(track), 3.65f, .95f, stretch: 1.38f);

    private void TickIslandFootsteps(MountedRacer racer, ref WaterFootsteps steps, float waterLevel, float dt)
    {
        if (!racer.UsesJetSki || racer.MountRecovery <= 0 || racer.JetSkiThrowAge >= 0 ||
            racer.Rider.TopLevel || racer.Rider.GetParent() != racer.Root || !racer.Rider.Visible ||
            racer.LeapHeight > .12f || racer.Speed < 2.3f ||
            racer.CoursePosition(track).DistanceSquaredTo(Player.CoursePosition(track)) > 65 * 65)
        {
            steps = default;
            return;
        }

        steps.SinceStep += dt;
        var rider = racer.Rider;
        Vector3 left = rider.Part(3).GlobalPosition.Lerp(rider.Part(4).GlobalPosition, .5f);
        Vector3 right = rider.Part(7).GlobalPosition.Lerp(rider.Part(8).GlobalPosition, .5f);
        float leftHeight = left.Y - waterLevel - WaveAt(left, RegattaWaterTime);
        float rightHeight = right.Y - waterLevel - WaveAt(right, RegattaWaterTime);
        bool leftWasPlanted = steps.LeftPlanted, rightWasPlanted = steps.RightPlanted;
        steps.LeftPlanted = leftHeight < (leftWasPlanted ? .27f : .17f);
        steps.RightPlanted = rightHeight < (rightWasPlanted ? .27f : .17f);
        bool leftStrike = steps.LeftPlanted && !leftWasPlanted;
        bool rightStrike = steps.RightPlanted && !rightWasPlanted;
        if (steps.SinceStep < .15f || (steps.SinceStep < .31f && !leftStrike && !rightStrike)) return;

        // Keep splashes on alternating steps when a native foot stays above the water plane.
        bool useRight = leftStrike == rightStrike ? steps.NextRight : rightStrike;
        Vector3 contact = useRight ? right : left;
        steps.NextRight = !useRight;
        steps.SinceStep = 0;
        float effort = Mathf.SmoothStep(3, 30, racer.Speed);
        EmitIslandSplash(contact, -racer.Heading(track), .48f + effort * .28f, .36f + effort * .16f,
            stretch: 1.22f, footstep: true);
    }

    private void SplashIslandShore(MountedRacer racer, Vector3 waterContact, float impactSpeed)
    {
        if (islandSplashes.Count == 0 || impactSpeed < 10) return;
        int index = racers.IndexOf(racer);
        if (index < 0 || index >= RacerCount || islandShoreSplashCooldown[index] > 0) return;
        islandShoreSplashCooldown[index] = .45f;
        float effort = Mathf.Clamp((impactSpeed - 10) / 45, 0, 1);
        EmitIslandSplash(waterContact, -racer.Heading(track), 1.7f + 2.0f * effort, .46f + .54f * effort);
    }

    private void SplashJetSkiLanding(MountedRacer racer, Vector3 waterContact, float impactSpeed)
    {
        if (islandSplashes.Count == 0 || impactSpeed < 2) return;
        int index = racers.IndexOf(racer);
        if (index < 0 || index >= RacerCount || islandLandingSplashCooldown[index] > 0) return;
        islandLandingSplashCooldown[index] = .26f;
        float effort = Mathf.Clamp((impactSpeed - 2) / 7, 0, 1);
        EmitIslandSplash(waterContact, racer.Heading(track), 2.3f + 2.8f * effort, .55f + .62f * effort,
            stretch: 1.45f);
    }

    private void EmitIslandSplash(Vector3 point, Vector3 direction, float radius, float energy, bool spray = true,
        bool mud = false, float stretch = 1, bool footstep = false)
    {
        if (islandSplashes.Count == 0 || track.WaterCourseLevel is not { } waterLevel ||
            racers.Count <= localRacerIndex || point.DistanceSquaredTo(Player.CoursePosition(track)) > 155 * 155)
            return;
        var splash = islandSplashes.FirstOrDefault(slot => !slot.Root.Visible);
        if (splash is null)
        {
            if (footstep) return;
            splash = islandSplashes.MinBy(slot => slot.Duration - slot.Age)!;
        }
        splash.Age = 0;
        splash.Duration = footstep ? .49f : spray ? 1.36f : .89f;
        splash.Radius = radius;
        splash.Stretch = stretch;
        splash.Root.GlobalPosition = point with { Y = waterLevel + .067f };
        splash.Ring.Rotation = Vector3.Up * Mathf.Atan2(direction.X, direction.Z);
        splash.Ring.Scale = new Vector3(radius * .21f, 1, radius * .21f * stretch);
        splash.Material.SetShaderParameter("progress", 0f);
        splash.Material.SetShaderParameter("regatta_time", RegattaWaterTime);
        splash.Material.SetShaderParameter("foam_color", mud
            ? new Color(.86f, .77f, .56f, Math.Min(.72f, energy))
            : new Color(.84f, .96f, 1, Math.Min(.79f, energy)));
        splash.Spray.Emitting = false;
        splash.Root.Show();
        if (!spray) return;
        float wave = WaveAt(point, RegattaWaterTime);
        splash.Spray.Position = Vector3.Up * (.09f + wave);
        // Ski slaps throw their spray forwards. Vertical golem breaches produce a wider plume.
        bool hullSlap = stretch > 1;
        splash.Spray.Direction = (Vector3.Up * (hullSlap ? .9f : 1.35f) + direction * .83f).Normalized();
        splash.Spray.Spread = hullSlap ? 35 : 55;
        splash.Spray.Amount = footstep ? 5 : 25;
        splash.Spray.Lifetime = footstep ? .31f : .56f;
        splash.Spray.ScaleAmountMin = footstep ? .22f : .42f;
        splash.Spray.ScaleAmountMax = footstep ? .58f : 1.4f;
        splash.Spray.InitialVelocityMin = footstep ? 1.25f : 1.8f + energy * 1.9f;
        splash.Spray.InitialVelocityMax = footstep ? 2.35f : 3.35f + energy * 4.4f;
        splash.Spray.Restart();
        splash.Spray.Emitting = true;
    }
}
