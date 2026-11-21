using Godot;

public partial class GrandPrix
{
    private const float VoidVolleyPeriod = 15.6f;
    private const float VoidWarningAt = .30f, VoidFireAt = 2.74f, VoidImpactAt = 3.49f, VoidClearAt = 4.98f;
    private const float VoidPrisonHitAt = VoidImpactAt + .41f;

    private enum VoidSpell { CrimsonBlast, BindingLight, BaelShockwave }

    [Flags]
    private enum VoidStrikeVisual { None = 0, Mark = 1, Orb = 2, Beam = 4, Burst = 8 }

    private readonly record struct VoidLapPattern(int RedSites, int GoldSites, int VioletSites, float Spread, float Power)
    {
        public int SiteLimit(VoidSpell spell) => spell switch
        {
            VoidSpell.CrimsonBlast => RedSites,
            VoidSpell.BindingLight => GoldSites,
            _ => VioletSites
        };
    }

    private static readonly VoidLapPattern[] VoidLapPatterns =
    [
        new(2, 2, 2, 1f, 1f),
        new(3, 2, int.MaxValue, 1f, 1.08f),
        new(3, int.MaxValue, 4, 1.13f, 1.15f),
        new(int.MaxValue, int.MaxValue, 5, 1.37f, 1.23f),
        new(int.MaxValue, int.MaxValue, int.MaxValue, 1.64f, 1.31f)
    ];

    private static readonly (Color Tint, string Warning, string Hit)[] VoidSpellLooks =
    [
        (new Color("ff384e"), "AERBAX · DODGE RED", "AERBAX BLAST!"),
        (new Color("ffe18f"), "ASHERON · DODGE GOLD", "CAUGHT IN THE BINDING!"),
        (new Color("c780ff"), "BAEL · DODGE VIOLET", "BAEL’S SHOCKWAVE!")
    ];

    private readonly record struct VoidHullEdge(Vector2 Inward, float Offset);
    private readonly record struct VoidStrikeTriangle(VoidHullEdge A, VoidHullEdge B, VoidHullEdge C,
        Vector3 Height, float Deck);

    private sealed class VoidStrikeSite(float station, float lane, float halfLane, Vector3 impact,
        float arrivalDelay, Basis deckBasis, VoidStrikeTriangle[] hitTriangles, float reachSquared,
        MeshInstance3D mark, ShaderMaterial material,
        MeshInstance3D orb, ShaderMaterial orbMaterial, MeshInstance3D beam,
        MeshInstance3D burst, StandardMaterial3D burstMaterial)
    {
        public readonly float Station = station, Lane = lane, HalfLane = halfLane;
        public readonly Vector3 Impact = impact;
        public readonly float ArrivalDelay = arrivalDelay, ReachSquared = reachSquared;
        public readonly Basis DeckBasis = deckBasis;
        public readonly VoidStrikeTriangle[] HitTriangles = hitTriangles;
        public readonly MeshInstance3D Mark = mark, Orb = orb, Beam = beam, Burst = burst;
        public readonly ShaderMaterial Material = material, OrbMaterial = orbMaterial;
        public readonly StandardMaterial3D BurstMaterial = burstMaterial;
        public VoidStrikeVisual Visuals;
    }

    private sealed class VoidAttack(VoidSpell spell, Vector3 source, Vector3 target, VoidStrikeSite[] sites)
    {
        public readonly VoidSpell Spell = spell;
        public readonly Vector3 Source = source, Target = target;
        public readonly VoidStrikeSite[] Sites = sites;
        public readonly List<VoidStrikeSite> Threats = new(sites.Length);
        public readonly List<VoidStrikeSite> ActiveStrikes = new(sites.Length);
        public readonly float LastArrival = sites.Max(site => site.ArrivalDelay);
        public int Volley = int.MinValue, HitMask, Lap = 1;
        public bool Warned;
        public bool HasVisuals;
    }

    private sealed record VoidBattleScene(Node3D Root, VoidAttack[] Attacks)
    {
        public float LastClock = float.NaN;
        public bool WasCountingDown;
        public bool Quiet;
        public int SharedPattern;
    }
    private VoidBattleScene? voidBattle;

    private void BuildVoidBattle(Node3D root, BogCircuit circuit)
    {
        var stage = new Node3D { Name = "Battle for the prison" };
        root.AddChild(stage);
        var markShader = GD.Load<Shader>("res://Shaders/VoidBattleMark.gdshader");
        var orbShader = GD.Load<Shader>("res://Shaders/VoidBattleOrb.gdshader");
        var sphere = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 13 };
        var cylinder = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 12 };
        var halo = new TorusMesh { InnerRadius = .84f, OuterRadius = 1, Rings = 48, RingSegments = 5 };
        var prison = Vector3.Up * (52 * VoidPrisonScale);
        var claimedStations = new List<float>();
        voidBattle = new VoidBattleScene(stage,
        [
            new(VoidSpell.CrimsonBlast, VoidAerbaxOrigin(VoidAerbaxPosition), prison,
                BuildSites(VoidSpell.CrimsonBlast, VoidAerbaxPosition, 0, -.66f, .73f)),
            new(VoidSpell.BindingLight, VoidAsheronCastOrigin(VoidAsheronPosition), prison,
                BuildSites(VoidSpell.BindingLight, VoidAsheronPosition, 0, .63f, -.71f)),
            new(VoidSpell.BaelShockwave, prison, Vector3.Zero,
                BuildSites(VoidSpell.BaelShockwave, Vector3.Back, -.31f, .39f, 2.77f, 3.53f))
        ]);

        VoidStrikeSite[] BuildSites(VoidSpell spell, Vector3 facing, params float[] bearings)
        {
            var sites = new List<VoidStrikeSite>();
            for (int ray = 0; ray < bearings.Length; ray++)
            {
                var bearing = (facing with { Y = 0 }).Normalized().Rotated(Vector3.Up, bearings[ray]);
                var crossings = FindVoidStrikeStations(circuit, bearing);
                if (spell == VoidSpell.BaelShockwave) Array.Reverse(crossings);
                for (int pass = 0; pass < crossings.Length; pass++)
                {
                    float station = crossings[pass];
                    if (claimedStations.Any(claimed => Math.Min(Math.Abs(claimed - station),
                            circuit.Length - Math.Abs(claimed - station)) < 58)) continue;
                    // The near deck catches the first blast. Its fallout then reaches the other level.
                    float delay = pass * .48f + Math.Min(ray, 2) * .23f;
                    sites.Add(BuildSite(spell, station, bearing, ray, pass, delay));
                    claimedStations.Add(station);
                }
            }
            if (sites.Count == 0) throw new InvalidDataException($"The arena road has no {spell} strike sites.");
            return sites.ToArray();
        }

        VoidStrikeSite BuildSite(VoidSpell spell, float station, Vector3 bearing, int ray, int pass, float delay)
        {
            float width = circuit.WidthAt(station);
            float side = circuit.Right(station).Dot(bearing) < 0 ? -1 : 1;
            if ((ray + pass) % 2 != 0 || spell == VoidSpell.BaelShockwave) side = -side;
            float lane = width * (spell == VoidSpell.BindingLight ? .2f : .43f) * side;
            float halfLane = width * (spell == VoidSpell.BindingLight ? .46f : .44f);
            float halfLength = spell == VoidSpell.BaelShockwave ? 22 : 17;
            var tint = VoidSpellLooks[(int)spell].Tint;
            Vector3 impact = circuit.Surface(station, lane);
            Vector3 forward = (circuit.Surface(station + 2, lane) - circuit.Surface(station - 2, lane)).Normalized();
            Vector3 right = (circuit.Surface(station, lane + 1) - circuit.Surface(station, lane - 1)).Normalized();
            Vector3 up = right.Cross(forward).Normalized();
            var material = new ShaderMaterial { Shader = markShader, RenderPriority = 1 };
            material.SetShaderParameter("tint", tint);
            material.SetShaderParameter("spell", (int)spell);
            var mesh = BuildVoidStrikeMesh(circuit, station, lane, halfLane, halfLength, impact,
                out var triangles, out float reachSquared);
            var mark = EffectNode(mesh, material, VoidSpellLooks[(int)spell].Warning);
            var orbPaint = new ShaderMaterial { Shader = orbShader };
            orbPaint.SetShaderParameter("tint", tint);
            var orb = EffectNode(sphere, orbPaint, "Spell bolt");
            var beam = EffectNode(cylinder, SoftGlow(new Color(tint, .23f)), "Spell stream");
            var burstPaint = SoftGlow(tint);
            var burst = EffectNode(halo, burstPaint, "Fallout flare");
            return new VoidStrikeSite(station, lane, halfLane, impact, delay, Basis.LookingAt(forward, up),
                triangles, reachSquared, mark, material, orb, orbPaint, beam, burst, burstPaint);
        }

        MeshInstance3D EffectNode(Mesh mesh, Material material, string name)
        {
            var node = BogCircuit.MeshAt(stage, mesh, Vector3.Zero, Vector3.One, material);
            node.Name = name;
            node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            node.Hide();
            return node;
        }

        static StandardMaterial3D SoftGlow(Color color) => new()
        {
            AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
    }

    private static float[] FindVoidStrikeStations(BogCircuit circuit, Vector3 bearing)
    {
        Vector3 lateral = bearing.Cross(Vector3.Up);
        int count = Mathf.CeilToInt(circuit.Length / 4);
        var crossings = new List<(float Station, float Radius)>();
        Vector3 previous = circuit.Point(0);
        float previousOffset = previous.Dot(lateral), previousStation = 0;
        for (int i = 1; i <= count; i++)
        {
            float at = i / (float)count * circuit.Length;
            Vector3 point = circuit.Point(at);
            float offset = point.Dot(lateral);
            if (offset > 0 && previousOffset <= 0 || offset < 0 && previousOffset >= 0)
            {
                float t = previousOffset / (previousOffset - offset);
                float radius = previous.Lerp(point, t).Dot(bearing);
                if (radius > 65) crossings.Add((Mathf.Lerp(previousStation, at, t), radius));
            }
            previous = point;
            previousOffset = offset;
            previousStation = at;
        }
        if (crossings.Count <= 1) return crossings.Select(crossing => crossing.Station).ToArray();
        crossings.Sort((first, second) => first.Radius.CompareTo(second.Radius));
        return [crossings[^1].Station, crossings[0].Station];
    }

    private static ArrayMesh BuildVoidStrikeMesh(BogCircuit circuit, float station, float lane, float halfLane,
        float halfLength, Vector3 impact, out VoidStrikeTriangle[] triangles, out float reachSquared)
    {
        const int steps = 16;
        var collision = new List<VoidStrikeTriangle>(steps * 2);
        var center = Flat(impact);
        reachSquared = 0;
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 previousLeft = default, previousRight = default;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float at = station + Mathf.Lerp(-halfLength, halfLength, t);
            Vector3 left = circuit.Surface(at, lane - halfLane), right = circuit.Surface(at, lane + halfLane);
            mesh.SetUV(new Vector2(0, t));
            mesh.AddVertex(left + Vector3.Up * .105f);
            mesh.SetUV(new Vector2(1, t));
            mesh.AddVertex(right + Vector3.Up * .105f);
            reachSquared = Mathf.Max(reachSquared,
                Mathf.Max(center.DistanceSquaredTo(Flat(left)), center.DistanceSquaredTo(Flat(right))));
            if (i > 0)
            {
                int a = (i - 1) * 2, b = i * 2;
                mesh.AddIndex(a); mesh.AddIndex(b); mesh.AddIndex(a + 1);
                mesh.AddIndex(a + 1); mesh.AddIndex(b); mesh.AddIndex(b + 1);
                AddCollision(previousLeft, left, previousRight);
                AddCollision(previousRight, left, right);
            }
            previousLeft = left;
            previousRight = right;
        }
        triangles = collision.ToArray();
        return mesh.Commit();

        void AddCollision(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = (b - a).Cross(c - a);
            if (Mathf.Abs(normal.Y) < .001f) return;
            // Dividing by Y makes the plane distance a vertical offset from this exact deck face.
            Vector3 height = normal / normal.Y;
            Vector2 x = Flat(a), y = Flat(b), z = Flat(c);
            collision.Add(new VoidStrikeTriangle(Edge(x, y, z), Edge(y, z, x), Edge(z, x, y), height, height.Dot(a)));
        }

        static VoidHullEdge Edge(Vector2 from, Vector2 to, Vector2 inside)
        {
            Vector2 delta = to - from;
            Vector2 inward = new Vector2(-delta.Y, delta.X).Normalized();
            if (inward.Dot(inside - from) < 0) inward = -inward;
            return new VoidHullEdge(inward, inward.Dot(from));
        }

        static Vector2 Flat(Vector3 point) => new(point.X, point.Z);
    }

    private void ResetVoidBattle()
    {
        if (!track.IsVoid || !VoidBattleIsReady()) return;
        var battle = voidBattle!;
        battle.Quiet = false;
        battle.SharedPattern = 0;
        foreach (var attack in battle.Attacks)
        {
            attack.HitMask = 0;
            attack.Volley = int.MinValue;
            attack.Lap = 1;
            attack.Warned = false;
        }
        TickVoidBattle(0);
    }

    private bool VoidBattleIsReady() => voidBattle is { Root: { } root } && GodotObject.IsInstanceValid(root) && !root.IsQueuedForDeletion();

    private int VoidBattleLap()
    {
        int lead = 1;
        foreach (var racer in racers) lead = Math.Max(lead, racer.GatesCleared);
        return Mathf.Clamp((lead - 1) / track.GateCount + 1, 1, VoidLapPatterns.Length);
    }

    private int CurrentVoidBattlePattern()
    {
        if (voidBattle is not { } battle) return 0;
        int pattern = 0;
        foreach (var attack in battle.Attacks) pattern |= attack.Lap << ((int)attack.Spell * 3);
        return pattern;
    }

    private void ApplyVoidBattlePattern(int pattern)
    {
        if (voidBattle is not { } battle || battle.SharedPattern == pattern) return;
        battle.SharedPattern = pattern;
        battle.LastClock = float.NaN;
    }

    private void QuietVoidBattleForFinale()
    {
        if (!VoidBattleIsReady()) return;
        var battle = voidBattle!;
        battle.Quiet = true;
        foreach (var attack in battle.Attacks)
        {
            foreach (var site in attack.Sites) SetVoidStrikeVisuals(site, VoidStrikeVisual.None);
            attack.HasVisuals = false;
            attack.Threats.Clear();
            attack.ActiveStrikes.Clear();
        }
        DrawVoidAerbax(0, 0, elapsed);
        DrawVoidAsheron(0, 0, elapsed);
    }

    private void TickVoidBattle(float dt)
    {
        if (!track.IsVoid || voidBattle is not { Quiet: false } battle) return;
        bool countingDown = countdown > 0;
        if (dt > 0 && elapsed == battle.LastClock && countingDown == battle.WasCountingDown) return;
        if (!VoidBattleIsReady()) return;
        battle.LastClock = elapsed;
        battle.WasCountingDown = countingDown;
        Vector3 eye = dt > 0 ? camera.Position : Vector3.Zero;
        int leadLap = VoidBattleLap();
        float bindingPulse = 0;
        foreach (var attack in battle.Attacks)
        {
            float time = elapsed - (int)attack.Spell * (VoidVolleyPeriod / 3);
            int volley = countingDown ? int.MinValue : Mathf.FloorToInt(time / VoidVolleyPeriod);
            float phase = countingDown || time < 0 ? -1 : time % VoidVolleyPeriod;
            if (attack.Volley != volley)
            {
                attack.Volley = volley;
                // A new lap cannot add strikes to a volley racers have already seen.
                attack.Lap = leadLap;
                attack.HitMask = 0;
                attack.Warned = false;
            }
            int sharedLap = (battle.SharedPattern >> ((int)attack.Spell * 3)) & 7;
            if (sharedLap >= 1 && sharedLap <= VoidLapPatterns.Length) attack.Lap = sharedLap;
            var pattern = VoidLapPatterns[attack.Lap - 1];
            float charge = phase is >= 0 and < VoidFireAt ? Mathf.SmoothStep(0, VoidFireAt, phase) : 0;
            float pulse = phase is >= VoidFireAt and < VoidImpactAt
                ? Mathf.Min(1, pattern.Power * (1 - Mathf.SmoothStep(VoidFireAt, VoidImpactAt, phase))) : 0;
            if (attack.Spell == VoidSpell.CrimsonBlast)
                DrawVoidAerbax(charge, pulse, elapsed, dt > 0 ? VoidPoseInterval(eye, attack.Source) : 0);
            if (attack.Spell == VoidSpell.BindingLight)
            {
                DrawVoidAsheron(charge, pulse, elapsed, dt > 0 ? VoidPoseInterval(eye, attack.Source) : 0);
                bindingPulse = phase is >= VoidPrisonHitAt and < VoidClearAt
                    ? Mathf.Min(1, pattern.Power * (1 - Mathf.SmoothStep(VoidPrisonHitAt, VoidClearAt, phase))) : 0;
            }

            attack.Threats.Clear();
            attack.ActiveStrikes.Clear();
            if (phase < VoidWarningAt || phase >= VoidClearAt + attack.LastArrival * pattern.Spread)
            {
                if (attack.HasVisuals)
                    foreach (var site in attack.Sites) SetVoidStrikeVisuals(site, VoidStrikeVisual.None);
                attack.HasVisuals = false;
                continue;
            }

            attack.HasVisuals = false;
            int siteLimit = pattern.SiteLimit(attack.Spell);
            for (int i = 0; i < attack.Sites.Length; i++)
            {
                var site = attack.Sites[i];
                if (i >= siteLimit)
                {
                    SetVoidStrikeVisuals(site, VoidStrikeVisual.None);
                    continue;
                }
                float arrival = site.ArrivalDelay * pattern.Spread;
                float sitePhase = phase - arrival;
                bool warning = sitePhase is >= VoidWarningAt and < VoidImpactAt;
                bool striking = sitePhase is >= VoidImpactAt and < VoidClearAt;
                if (warning || striking) attack.Threats.Add(site);
                if (striking) attack.ActiveStrikes.Add(site);
                DrawVoidStrike(attack, site, sitePhase, arrival, pattern.Power, warning, striking);
                attack.HasVisuals |= site.Visuals != VoidStrikeVisual.None;
                if (!warning || attack.Warned || Player.Finish >= 0) continue;
                float ahead = AheadOnCourse(Player.Distance, site.Station);
                if (ahead > 114 && ahead < track.Length - 18) continue;
                attack.Warned = true;
                Callout(VoidSpellLooks[(int)attack.Spell].Warning, 1.48f);
            }
        }
        DrawVoidClashPulse(bindingPulse, elapsed);
    }

    private static void DrawVoidStrike(VoidAttack attack, VoidStrikeSite site, float phase, float arrival, float power,
        bool warning, bool striking)
    {
        bool primary = site == attack.Sites[0], returnsToPrison = primary && attack.Spell != VoidSpell.BaelShockwave;
        float fire = VoidFireAt - arrival * .65f;
        bool flying = phase >= fire && phase < (returnsToPrison ? VoidPrisonHitAt : VoidImpactAt);
        bool marked = warning || striking;
        var visuals = marked ? VoidStrikeVisual.Mark : VoidStrikeVisual.None;
        if (flying) visuals |= VoidStrikeVisual.Orb;
        if (striking) visuals |= VoidStrikeVisual.Burst;
        if (marked)
        {
            site.Material.SetShaderParameter(VoidUniform.Phase, phase);
            site.Material.SetShaderParameter(VoidUniform.Pulse, .55f + .45f * Mathf.Sin(phase * (warning ? 10 : 24)));
            if ((site.Visuals & (VoidStrikeVisual.Mark | VoidStrikeVisual.Burst)) !=
                (visuals & (VoidStrikeVisual.Mark | VoidStrikeVisual.Burst)))
                site.Material.SetShaderParameter(VoidUniform.ImpactActive, striking ? 1f : 0f);
        }
        if (!flying && !striking)
        {
            SetVoidStrikeVisuals(site, visuals);
            return;
        }

        Vector3 strike = site.Impact + site.DeckBasis.Y * 2.65f;
        float travel = Mathf.Clamp((phase - fire) / (VoidImpactAt - fire), 0, 1);
        Vector3 front = attack.Source.Lerp(strike, travel);
        front.Y += Mathf.Sin(travel * Mathf.Pi) * 5;
        if (attack.Spell == VoidSpell.BaelShockwave) front.Y += 3.0f * Mathf.Sin(travel * Mathf.Pi);
        if (travel >= 1 && returnsToPrison)
            front = strike.Lerp(attack.Target, Mathf.Clamp((phase - VoidImpactAt) / (VoidPrisonHitAt - VoidImpactAt), 0, 1));

        float radius = attack.Spell == VoidSpell.CrimsonBlast ? 6.3f : attack.Spell == VoidSpell.BindingLight ? 4.2f : 5.5f;
        if (!primary) radius *= .72f;
        if (flying)
        {
            site.Orb.Position = front;
            site.Orb.Scale = Vector3.One * (radius * power * (.93f + .12f * Mathf.Sin(phase * 21)));
            site.OrbMaterial.SetShaderParameter(VoidUniform.Phase, phase);
        }
        Vector3 end = flying ? front : strike;
        Vector3 origin = travel < 1 ? attack.Source : strike;
        float tail = attack.Spell == VoidSpell.BindingLight ? 98 : 69;
        Vector3 start = flying
            ? front.Lerp(origin, Math.Min(1, tail / Math.Max(1, front.DistanceTo(origin))))
            : strike + site.DeckBasis.Y * Mathf.Lerp(20, 3, (phase - VoidImpactAt) / (VoidClearAt - VoidImpactAt));
        float beamWidth = power * (attack.Spell == VoidSpell.BindingLight ? 1.9f : 1.13f) * (primary ? 1 : .66f);
        if (PlaceVoidBeam(site.Beam, start, end, beamWidth)) visuals |= VoidStrikeVisual.Beam;
        if (striking)
        {
            float age = (phase - VoidImpactAt) / (VoidClearAt - VoidImpactAt);
            float size = Mathf.Lerp(3.5f, site.HalfLane * 1.65f, Mathf.SmoothStep(0, .85f, age));
            site.Burst.Transform = new Transform3D(site.DeckBasis * Basis.FromScale(new Vector3(size, 1.85f, size)),
                site.Impact + site.DeckBasis.Y * (.36f + age * 3.4f));
            site.BurstMaterial.AlbedoColor = new Color(VoidSpellLooks[(int)attack.Spell].Tint, Mathf.Min(1, (.98f - .65f * age) * power));
        }
        SetVoidStrikeVisuals(site, visuals);
    }

    private static void SetVoidStrikeVisuals(VoidStrikeSite site, VoidStrikeVisual visuals)
    {
        var changed = site.Visuals ^ visuals;
        if ((changed & VoidStrikeVisual.Mark) != 0) site.Mark.Visible = (visuals & VoidStrikeVisual.Mark) != 0;
        if ((changed & VoidStrikeVisual.Orb) != 0) site.Orb.Visible = (visuals & VoidStrikeVisual.Orb) != 0;
        if ((changed & VoidStrikeVisual.Beam) != 0) site.Beam.Visible = (visuals & VoidStrikeVisual.Beam) != 0;
        if ((changed & VoidStrikeVisual.Burst) != 0) site.Burst.Visible = (visuals & VoidStrikeVisual.Burst) != 0;
        site.Visuals = visuals;
    }

    private static bool PlaceVoidBeam(MeshInstance3D node, Vector3 from, Vector3 to, float radius)
    {
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < .01f) return false;
        Vector3 axis = delta / length;
        Vector3 side = axis.Cross(Math.Abs(axis.Dot(Vector3.Back)) > .95f ? Vector3.Right : Vector3.Back).Normalized();
        node.Transform = new Transform3D(new Basis(side * radius, axis * length, side.Cross(axis) * radius), (from + to) * .5f);
        return true;
    }

    private void SenseVoidBattleObstacles(MountedRacer racer, List<RivalObstacle> obstacles)
    {
        if (!track.IsVoid || voidBattle is not { } battle) return;
        foreach (var attack in battle.Attacks)
        foreach (var site in attack.Threats)
        {
            float ahead = AheadOnCourse(racer.Distance, site.Station);
            if (ahead <= 88) obstacles.Add(new RivalObstacle(site.Lane, ahead, site.HalfLane, Crossing: true));
        }
    }

    private void CheckVoidBattleHits(MountedRacer racer, int index, Vector3 before, Vector3 after)
    {
        if (!track.IsVoid || voidBattle is not { } battle || racer.EjectTime > 0 || racer.Finish >= 0) return;
        if (index < 0) return;
        int bit = 1 << index;
        foreach (var attack in battle.Attacks)
        {
            if ((attack.HitMask & bit) != 0) continue;
            foreach (var site in attack.ActiveStrikes)
            {
                if (!VoidStrikeTouches(site, before, after)) continue;
                if (racer.Flight?.DodgingRoll == true)
                {
                    attack.HitMask |= bit;
                    if (racer == Player) Callout("BARREL ROLL DODGE!", 1.05f);
                    break;
                }
                float ward = racer.Ward, guard = racer.GuardWindow;
                if (!AcceptHit(racer, allowFlight: true, impact: ShieldImpact.Spell))
                {
                    if (racer.Ward < ward || racer.GuardWindow < guard) attack.HitMask |= bit;
                    break;
                }
                attack.HitMask |= bit;
                var (tint, _, message) = VoidSpellLooks[(int)attack.Spell];
                Wave(after + Vector3.Up * .55f, tint, 3.8f, .62f);
                if (attack.Spell == VoidSpell.BindingLight)
                {
                    racer.Speed *= .53f;
                    racer.Boost = 0;
                    racer.Slowed = Math.Max(racer.Slowed, 1.85f);
                }
                else
                {
                    Vector3 push = FlatDirection(attack.Source, site.Impact).Normalized();
                    racer.Draw(track, 0, true);
                    if (racer.Flight is { } ride)
                    {
                        Transform3D airPose = racer.Rider.GlobalTransform;
                        racer.Land();
                        racer.Eject(track, push, airPose, ride.Heading);
                    }
                    else racer.Eject(track, push);
                    racer.Stability = 0;
                    racer.Speed = racer.Boost = 0;
                }
                if (racer == Player) Callout(message, 1.42f);
                return;
            }
        }
    }

    private static bool VoidStrikeTouches(VoidStrikeSite site, Vector3 before, Vector3 after)
    {
        Vector2 start = new(before.X, before.Z), end = new(after.X, after.Z);
        Vector2 center = new(site.Impact.X, site.Impact.Z);
        Vector2 motion = end - start;
        float nearest = Math.Clamp((center - start).Dot(motion) / Math.Max(.00001f, motion.LengthSquared()), 0, 1);
        if (center.DistanceSquaredTo(start + motion * nearest) > site.ReachSquared + .01f) return false;
        foreach (var triangle in site.HitTriangles)
        {
            float enter = 0, exit = 1;
            if (!OnDeck(triangle.A, ref enter, ref exit) ||
                !OnDeck(triangle.B, ref enter, ref exit) ||
                !OnDeck(triangle.C, ref enter, ref exit)) continue;
            float from = triangle.Height.Dot(before) - triangle.Deck;
            float to = triangle.Height.Dot(after) - triangle.Deck;
            if (Clip(from + .3f, to + .3f, ref enter, ref exit) &&
                Clip(5.6f - from, 5.6f - to, ref enter, ref exit)) return true;
        }
        return false;

        bool OnDeck(VoidHullEdge edge, ref float enter, ref float exit) =>
            Clip(edge.Inward.Dot(start) - edge.Offset, edge.Inward.Dot(end) - edge.Offset, ref enter, ref exit);

        static bool Clip(float first, float last, ref float enter, ref float exit)
        {
            if (first >= 0 && last >= 0) return true;
            if (first < 0 && last < 0) return false;
            float crossing = first / (first - last);
            if (first < 0) enter = Math.Max(enter, crossing);
            else exit = Math.Min(exit, crossing);
            return enter <= exit;
        }
    }

    private void CheckVoidBattle()
    {
        Require(track.IsVoid && VoidBattleIsReady(), "the void battle stages on the normal race course");
        var battle = voidBattle!;
        int siteCount = battle.Attacks.Sum(attack => attack.Sites.Length);
        Require(siteCount >= 14, $"battle fallout reaches both orbits of the XL road ({siteCount} strike zones)");

        ResetRace();
        countdown = 0;
        elapsed = 2.4f;
        TickVoidBattle(0);
        var red = battle.Attacks[(int)VoidSpell.CrimsonBlast];
        Require(red.Threats.Count == 2 && VoidBattleLap() == 1, "first lap has the opening volley");
        racers[^1].GatesCleared = 4 * track.GateCount + 1;
        elapsed += .1f;
        TickVoidBattle(0);
        Require(red.Threats.Count == 2 && VoidBattleLap() == 5, "a lap leader can advance the fight without adding unannounced strikes");
        elapsed = VoidVolleyPeriod + 2.4f;
        TickVoidBattle(0);
        Require(red.Threats.Count == red.Sites.Length && red.Lap == 5, "the fifth lap's next volley lights up all of its strike sites");
        int finalPattern = CurrentVoidBattlePattern();
        Require(red.Lap == (finalPattern & 7), "relay pattern carries the authoritative attack stage");
        QuietVoidBattleForFinale();
        Require(red.Threats.Count == 0 && red.Sites.All(site => site.Visuals == VoidStrikeVisual.None),
            "the finish stops outstanding attacks");

        ResetRace();
        countdown = 0;
        elapsed = 2.4f;
        TickVoidBattle(0);
        ApplyVoidBattlePattern(finalPattern);
        TickVoidBattle(1f / 60);
        Require(red.Threats.Count == red.Sites.Length && VoidBattleLap() == 1,
            "a guest uses the host's active volley pattern even if its racers are behind");

        foreach (var attack in battle.Attacks)
        foreach (var site in attack.Sites)
        {
            ResetRace();
            countdown = 0;
            Player.GatesCleared = 4 * track.GateCount + 1;
            Player.Distance = site.Station;
            Player.Lane = site.Lane;
            Player.Speed = 23;
            var pass = track.Surface(site.Station, site.Lane);
            var before = track.Surface(site.Station - 15, site.Lane);
            float start = (int)attack.Spell * (VoidVolleyPeriod / 3) + site.ArrivalDelay * VoidLapPatterns[^1].Spread;
            elapsed = start + 1.4f;
            TickVoidBattle(0);
            CheckVoidBattleHits(Player, localRacerIndex, before, pass);
            Require(site.Mark.Visible && !site.Burst.Visible && Player.EjectTime == 0 && Player.Slowed == 0,
                "an incoming spell paints a warning before its hit window");
            Player.Distance = site.Station - 24;
            var sensed = new List<RivalObstacle>();
            SenseVoidBattleObstacles(Player, sensed);
            Player.Distance = site.Station;
            Require(sensed.Any(o => o.Crossing && Mathf.IsEqualApprox(o.Lane, site.Lane)),
                "rivals can sense the warned lane on their own road level");
            elapsed = start + VoidImpactAt + .16f;
            TickVoidBattle(0);
            Require(VoidStrikeTouches(site, before, pass) &&
                    !VoidStrikeTouches(site, before + Vector3.Up * 18, pass + Vector3.Up * 18) &&
                    !VoidStrikeTouches(site, before - Vector3.Up * 18, pass - Vector3.Up * 18),
                "battle fallout follows the marked road grade and leaves the other level clear");
            float safeLane = (site.Lane < 0 ? 1 : -1) * (track.HalfWidth - 2);
            CheckVoidBattleHits(Player, localRacerIndex, track.Surface(site.Station - 15, safeLane), track.Surface(site.Station, safeLane));
            Require(site.Burst.Visible && Player.EjectTime == 0 && Player.Slowed == 0, "a marked spell leaves a viable safe lane");
            Player.Ward = 1;
            CheckVoidBattleHits(Player, localRacerIndex, before, pass);
            Require(Player.Ward == 0 && Player.EjectTime == 0 && Player.Slowed == 0, "a ward absorbs battle fallout");
            attack.HitMask = 0;
            CheckVoidBattleHits(Player, localRacerIndex, before, pass);
            Require(attack.Spell == VoidSpell.BindingLight ? Player.Slowed > 1 : Player.EjectTime > 0,
                "battle fallout slows or dismounts an exposed racer");
        }
        ResetRace();
        GD.Print($"VOID BATTLE PASS | five lap patterns, {siteCount} deck-aware strike zones, open lanes, ward blocks and racing impacts.");
    }
}
