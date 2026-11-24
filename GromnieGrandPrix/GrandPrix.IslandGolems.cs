using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private const float IslandGolemQuiet = .8f;
    private const float IslandGolemWarning = 2.35f;
    private const float IslandGolemCrossing = 3.15f;
    private const float IslandGolemRise = .56f;
    private const float IslandGolemSink = .64f;
    private static readonly Color IslandWaterGolemTint = new("85e9ff"), IslandMudGolemTint = new("edbe75");
    private static readonly RareCatalog.Creature IslandWaterGolem = new()
    {
        // The original water setup is made of effect planes. Give it the native elemental body
        // and its own moving water surface so racers can read its silhouette against the sea.
        Name = "Water Golem", Setup = 0x020007d7, Motion = 0x09000081, Sound = 0x2000009b
    };
    private static readonly RareCatalog.Creature IslandMudGolem = new()
    {
        Id = 200, Name = "Mud Golem", Setup = 0x020007ca, Motion = 0x09000081,
        Sound = 0x20000099, Palette = 0x04000f46, Clothing = 0x1000020e,
        PaletteTemplate = 4, Shade = .5f
    };

    private sealed class IslandGolem(AcActor actor, Label3D sign, MeshInstance3D ring,
        float distance, float reach, float radius, bool mud, Vector3 center, float period, float offset)
    {
        public AcActor Actor { get; } = actor;
        public Label3D Sign { get; } = sign;
        public MeshInstance3D Ring { get; } = ring;
        public float Distance { get; } = distance;
        public float Reach { get; } = reach;
        public float Radius { get; } = radius;
        public bool Mud { get; } = mud;
        public Vector3 Center { get; } = center;
        public float Period { get; } = period;
        public float Offset { get; } = offset;
        public Vector3 Before, Position, Heading;
        public int Cycle = -1, HitMask;
        public bool Dangerous, Warned, Swung, Breached, Sunk;
        public float RippleAt;
    }

    private readonly record struct IslandGolemPose(int Cycle, float Phase, float Lane, float Lift, bool Crossing)
    {
        public bool Warning => Phase is >= 0 and < IslandGolemWarning;
        public bool Visible => Phase >= IslandGolemWarning &&
            Phase < IslandGolemWarning + IslandGolemCrossing + IslandGolemSink;
    }

    private readonly List<IslandGolem> islandGolems = [];

    private void BuildIslandGolems()
    {
        islandGolems.Clear();
        if (!track.IsWaterCourse) return;
        var root = new Node3D { Name = "Water and mud golem crossings" };
        courseItems.AddChild(root);
        var ringMesh = new TorusMesh { InnerRadius = .90f, OuterRadius = 1, Rings = 26, RingSegments = 5 };
        var waterGlow = PickupColor("85e9ff");
        var mudGlow = PickupColor("edbe75");
        var rippleTexture = assets.Texture(0x0500146a);
        waterGlow.AlbedoTexture = mudGlow.AlbedoTexture = rippleTexture;
        var waterBody = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/IslandGolemWater.gdshader") };
        uint waterTexture = townRegion!.Terrains.First(terrain => terrain.Name == "WaterShallowSea").Texture;
        if (assets.Texture(waterTexture) is { } nativeWater) waterBody.SetShaderParameter("native_water", nativeWater);
        int count = Mathf.Clamp(Mathf.RoundToInt(track.Length / 420), 4, 9);
        float spacing = track.Length / count;
        for (int i = 0; i < count; i++)
        {
            bool mud = i % 2 == 1;
            float preferred = (i + .48f) * spacing;
            for (int offset = 0; offset < 11; offset++)
            {
                float at = preferred + ((offset + 1) / 2) * (offset % 2 == 1 ? -13 : 13);
                float side = i % 4 < 2 ? -1 : 1;
                if (!TryIslandGolemReach(at, side, mud, out float reach) &&
                    !TryIslandGolemReach(at, -side, mud, out reach)) continue;

                var look = mud ? IslandMudGolem : IslandWaterGolem;
                var actor = assets.CreateActor(look.Setup, look.Motion, mud ? 1.5f : 1.56f);
                actor.Name = look.Name;
                actor.ApplyNativeAppearance(look);
                if (!mud)
                    foreach (var mesh in actor.BodyMeshes) mesh.MaterialOverride = waterBody;
                actor.SoundTable = look.Sound;
                actor.OnHook = (hook, _) => raceAudio?.PlayEmote(hook, actor);
                root.AddChild(actor);

                float warningLane = reach * (mud ? .50f : .87f);
                var sign = new Label3D
                {
                    Name = look.Name + " warning",
                    Position = track.Surface(at - 7, warningLane) + Vector3.Up * 2.8f,
                    Text = mud ? "MUD GOLEM" : "WATER GOLEM",
                    FontSize = 58, PixelSize = .025f, OutlineSize = 7,
                    Modulate = mud ? IslandMudGolemTint : IslandWaterGolemTint,
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled
                };
                root.AddChild(sign);
                var ring = BogCircuit.MeshAt(root, ringMesh, Vector3.Zero, Vector3.One, mud ? mudGlow : waterGlow);
                ring.Name = "Breach warning";
                ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                float radius = Mathf.Clamp(track.WidthAt(at) * .17f, 1.9f, mud ? 3.45f : 2.6f);
                float period = 10.3f + i % 4 * 1.05f;
                // Each crossing has a different rhythm, but guests can reproduce it from race time alone.
                float phaseOffset = Mathf.PosMod(period * i * .381966f, period);
                islandGolems.Add(new(actor, sign, ring, at, reach, radius, mud, track.Point(at), period, phaseOffset));
                break;
            }
        }
        ResetIslandGolems();
    }

    private bool TryIslandGolemReach(float at, float side, bool mud, out float reach)
    {
        float width = track.WidthAt(at);
        reach = side * width * .95f;
        if (width < 9 || Math.Abs(track.Curvature(at)) > .023f ||
            Enumerable.Range(0, track.GateCount).Any(gate => Close(track.Gate(gate), 40)) ||
            pickups.Any(pickup => Close(pickup.Distance, 21)) || pads.Any(pad => Close(pad.Distance, 15)))
            return false;

        for (int i = 0; i <= 6; i++)
        {
            float lane = Mathf.Lerp(reach, reach * (mud ? .45f : -1), i / 6f);
            if (!track.NavigableWater(track.Surface(at, lane))) return false;
        }
        return true;

        bool Close(float distance, float margin) =>
            Math.Abs(Mathf.Wrap(at - distance, -track.Length * .5f, track.Length * .5f)) < margin;
    }

    private void ResetIslandGolems()
    {
        foreach (var site in islandGolems)
        {
            site.Cycle = -1;
            site.HitMask = 0;
            site.Dangerous = site.Warned = site.Swung = site.Breached = site.Sunk = false;
            site.RippleAt = 0;
            site.Actor.Hide();
            site.Sign.Hide();
            site.Ring.Hide();
        }
    }

    private IslandGolemPose IslandGolemAt(IslandGolem site, float time)
    {
        float timeAtGolem = time + site.Offset;
        int cycle = Mathf.FloorToInt(timeAtGolem / site.Period);
        float phase = timeAtGolem - cycle * site.Period - IslandGolemQuiet;
        float across = phase - IslandGolemWarning;
        float travel = Mathf.SmoothStep(0, 1, across / (IslandGolemCrossing * (site.Mud ? .51f : 1)));
        float lane = Mathf.Lerp(site.Reach, site.Reach * (site.Mud ? .45f : -1), travel);
        float height = 1 - Mathf.SmoothStep(0, IslandGolemRise, across) +
            Mathf.SmoothStep(IslandGolemCrossing, IslandGolemCrossing + IslandGolemSink, across);
        return new(cycle, phase, lane, -height * 3.2f, across >= IslandGolemRise && across < IslandGolemCrossing);
    }

    private void TickIslandGolems(float dt)
    {
        if (islandGolems.Count == 0 || countdown > 0 || racers.Count <= localRacerIndex) return;
        Vector3 eye = Player.CoursePosition(track);
        foreach (var site in islandGolems)
        {
            var pose = IslandGolemAt(site, elapsed);
            if (pose.Cycle != site.Cycle)
            {
                site.Cycle = pose.Cycle;
                site.HitMask = 0;
                site.Warned = site.Swung = site.Breached = site.Sunk = false;
                site.RippleAt = 0;
            }
            site.Before = site.Position;
            site.Position = track.Surface(site.Distance, pose.Lane);
            if (!site.Dangerous) site.Before = site.Position;
            site.Heading = -Math.Sign(site.Reach) * track.Right(site.Distance);
            site.Dangerous = pose.Crossing;

            bool nearby = site.Center.DistanceSquaredTo(eye) < 175 * 175;
            site.Actor.Visible = nearby && pose.Visible;
            site.Sign.Visible = nearby && pose.Warning;
            site.Ring.Visible = nearby && pose.Warning;
            if (!nearby) continue;

            if (pose.Warning)
            {
                float warningLane = site.Reach * (site.Mud ? .50f : .87f);
                Vector3 anchor = track.Surface(site.Distance, warningLane);
                site.Ring.Position = anchor + Vector3.Up * (WaveAt(anchor, RegattaWaterTime) + .16f);
                float ripple = 1 + .18f * Mathf.Sin(pose.Phase * 12);
                site.Ring.Scale = new Vector3(site.Radius * ripple, .06f, site.Radius * ripple);
                if (!site.Warned && AheadOnCourse(Player.Distance, site.Distance) is >= 4 and <= 110)
                {
                    site.Warned = true;
                    Callout(site.Mud ? "MUD GOLEM AHEAD!" : "WATER GOLEM CROSSING!", 1.15f);
                }
                continue;
            }
            if (!pose.Visible) continue;

            site.Actor.Position = site.Position + Vector3.Up * (pose.Lift + WaveAt(site.Position, RegattaWaterTime));
            site.Actor.Face(site.Heading);
            float breachTime = pose.Phase - IslandGolemWarning;
            if (!site.Breached && breachTime is >= .12f and <= 1.35f)
            {
                site.Breached = true;
                site.RippleAt = breachTime + .34f;
                ImpactRegattaWater(site.Position, site.Radius * 1.40f, .30f);
                EmitIslandSplash(site.Position, site.Heading, site.Radius * 1.85f, 1.13f, mud: site.Mud);
            }
            else if (breachTime >= IslandGolemCrossing && !site.Sunk)
            {
                site.Sunk = true;
                ImpactRegattaWater(site.Position, site.Radius * 1.16f, .20f);
                EmitIslandSplash(site.Position, -site.Heading, site.Radius * 1.55f, .76f, mud: site.Mud);
            }
            else if (pose.Crossing && breachTime >= site.RippleAt)
            {
                site.RippleAt = breachTime + .34f;
                EmitIslandSplash(site.Position, site.Heading, site.Radius * 1.32f, .37f, spray: false, mud: site.Mud);
            }
            if (site.Mud && !site.Swung && pose.Phase > IslandGolemWarning + .92f)
            {
                site.Swung = true;
                site.Actor.AttackCreature(0x10000063, .95f);
            }
            bool moving = pose.Crossing && (!site.Mud || pose.Phase < IslandGolemWarning + 1.28f);
            site.Actor.Tick(dt, moving, site.Heading);
        }
    }

    private void CheckIslandGolemHits(MountedRacer racer, int index, Vector3 before, Vector3 after)
    {
        if (racer.Finish >= 0 || racer.EjectTime > 0 || racer.Flight is not null) return;
        int bit = 1 << index;
        foreach (var site in islandGolems)
        {
            if (!site.Dangerous || (site.HitMask & bit) != 0 || after.DistanceSquaredTo(site.Center) > 40 * 40) continue;
            Vector3 offset = before - site.Before;
            Vector3 travel = after - site.Position - offset;
            var start = new Vector2(offset.X, offset.Z);
            var step = new Vector2(travel.X, travel.Z);
            float t = Mathf.Clamp(-start.Dot(step) / Math.Max(.00001f, step.LengthSquared()), 0, 1);
            if ((start + step * t).LengthSquared() > site.Radius * site.Radius || Math.Abs(offset.Y + travel.Y * t) > 1.70f)
                continue;

            if (!AcceptHit(racer))
            {
                if (racer.ItemGrace <= 0 && !racer.DodgesItems) site.HitMask |= bit;
                continue;
            }
            site.HitMask |= bit;
            ClearRacerPowers(racer);
            racer.Stability = 0;
            racer.Draw(track, 0, false);
            racer.Eject(track, site.Heading);
            racer.Speed = racer.Boost = 0;
            if (racer == Player)
                Callout(site.Mud ? "DUMPED BY A MUD GOLEM!" : "SWEPT OFF YOUR SKI!", 1.7f);
            return;
        }
    }

    private void SenseIslandGolems(MountedRacer racer, List<RivalObstacle> obstacles)
    {
        foreach (var site in islandGolems)
        {
            float ahead = AheadOnCourse(racer.Distance, site.Distance);
            if (ahead < 0 || ahead > 150) continue;
            var pose = IslandGolemAt(site, elapsed + ahead / Math.Max(8, racer.Speed));
            if (pose.Crossing) obstacles.Add(new(pose.Lane, ahead, site.Radius, Crossing: true));
        }
    }
}
