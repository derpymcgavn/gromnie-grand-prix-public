using Godot;
using DerethSurvivors;

public partial class GrandPrix
{
    private sealed record HazardKind(string Name, uint Effects, Color Tint);
    private static readonly HazardKind[] HazardKinds =
    [
        new("FIRE VENT", AcCatalog.Projectiles["fire"].Effects, new("ff8047")),
        new("FROST VENT", AcCatalog.Projectiles["frost"].Effects, new("90e6ff")),
        new("STATIC SURGE", AcCatalog.Projectiles["lightning"].Effects, new("d9a4ff"))
    ];
    private sealed class RoadHazard(Vector3 center, float radius, HazardKind kind, MeshInstance3D marker)
    {
        public readonly Vector3 Center = center;
        public readonly float Radius = radius;
        public readonly HazardKind Kind = kind;
        public readonly MeshInstance3D Marker = marker;
        public float PulseAt = float.PositiveInfinity, ActiveUntil;
        public int HitMask;
    }

    private readonly List<RoadHazard> roadHazards = [];
    private readonly StandardMaterial3D hazardIdle = PickupColor("c38c4e"), hazardWarning = PickupColor("ffd784");
    private readonly TorusMesh hazardRing = new() { InnerRadius = .94f, OuterRadius = 1, Rings = 30, RingSegments = 5 };
    private const float HazardWarningTime = 1.25f, HazardActiveTime = 1.15f;

    private void BuildRoadHazards()
    {
        roadHazards.Clear();
        if (track.IsVoid || track.IsWaterCourse) return;
        var random = onlinePlan is { } plan ? new Random(plan.Seed ^ 7241) : testMode ? new Random(7241 + activeTown) : Random.Shared;
        int count = Mathf.Clamp(Mathf.CeilToInt(track.Length / 315), 2, 16);
        float spacing = track.Length / count;
        float radius = Mathf.Clamp(track.HalfWidth * .18f, .74f, 2.35f);
        for (int i = 0; i < count; i++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float at = BogCircuit.Grid + (i + .32f + random.NextSingle() * .30f) * spacing;
                float buffer = Math.Min(15, track.Length / track.GateCount * .20f);
                if (Enumerable.Range(0, track.GateCount).Any(gate => Near(track.Gate(gate), buffer)) ||
                    pads.Any(pad => Near(pad.Distance, 7)) || pickups.Any(pickup => Near(pickup.Distance, 7))) continue;
                if (flyover is { } flight && at > flight.Start - 17 && at < flight.End + 17) continue;
                float lane = track.HalfWidth * (random.Next(2) == 0 ? -.57f : .57f);
                var center = track.Surface(at, lane);
                var kind = HazardKinds[random.Next(HazardKinds.Length)];
                var marker = BogCircuit.MeshAt(roadsideRoot, hazardRing, center + Vector3.Up * .12f, Vector3.One * radius, hazardIdle);
                marker.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                float slope = (track.Surface(at + 1, lane).Y - track.Surface(at - 1, lane).Y) * .5f;
                float bank = (track.Surface(at, lane + 1).Y - track.Surface(at, lane - 1).Y) * .5f;
                marker.Rotation = new Vector3(Mathf.Atan(slope), BogCircuit.Yaw(track.Forward(at)), Mathf.Atan(bank));
                roadHazards.Add(new RoadHazard(center, radius, kind, marker));
                break;

                bool Near(float distance, float margin) => Math.Abs(Mathf.Wrap(at - distance, -track.Length * .5f, track.Length * .5f)) < margin;
            }
        }
    }

    private void TickRoadHazards(Vector3 player)
    {
        Span<Vector3> rivals = stackalloc Vector3[RacerCount - 1];
        int count = 0;
        for (int i = 1; i < racers.Count; i++)
            if (racers[i].Finish < 0 && racers[i].Flight is null) rivals[count++] = racers[i].CoursePosition(track);
        foreach (var hazard in roadHazards)
        {
            bool nearPlayer = hazard.Center.DistanceSquaredTo(player) <= 79 * 79;
            bool occupied = nearPlayer;
            for (int i = 0; i < count && !occupied; i++) occupied = hazard.Center.DistanceSquaredTo(rivals[i]) <= 79 * 79;
            if (!occupied)
            {
                hazard.PulseAt = float.PositiveInfinity;
                hazard.ActiveUntil = 0;
                hazard.Marker.MaterialOverride = hazardIdle;
                continue;
            }
            if (float.IsPositiveInfinity(hazard.PulseAt)) hazard.PulseAt = elapsed + HazardWarningTime;
            if (elapsed >= hazard.PulseAt)
            {
                hazard.ActiveUntil = elapsed + HazardActiveTime;
                hazard.PulseAt = elapsed + 4.6f;
                hazard.HitMask = 0;
                if (nearPlayer)
                {
                    itemEffects?.PlayAt(hazard.Kind.Effects, 5, hazard.Center + Vector3.Up * .66f);
                    Wave(hazard.Center + Vector3.Up * .15f, hazard.Kind.Tint, hazard.Radius * 1.14f, .80f);
                }
            }
            bool active = elapsed < hazard.ActiveUntil;
            bool warning = hazard.PulseAt - elapsed <= HazardWarningTime;
            hazard.Marker.MaterialOverride = warning || active ? hazardWarning : hazardIdle;
            float size = hazard.Radius * (warning ? 1 + Mathf.Sin(elapsed * 18) * .07f : 1);
            hazard.Marker.Scale = Vector3.One * size;
        }
    }

    private void CheckRoadHazards(MountedRacer racer, int index, Vector3 before, Vector3 after)
    {
        int bit = 1 << index;
        foreach (var hazard in roadHazards)
        {
            if (elapsed >= hazard.ActiveUntil || (hazard.HitMask & bit) != 0) continue;
            var offset = new Vector2(before.X - hazard.Center.X, before.Z - hazard.Center.Z);
            var travel = new Vector2(after.X - before.X, after.Z - before.Z);
            float t = Mathf.Clamp(-offset.Dot(travel) / Math.Max(.00001f, travel.LengthSquared()), 0, 1);
            if ((offset + travel * t).LengthSquared() > hazard.Radius * hazard.Radius ||
                Math.Abs(Mathf.Lerp(before.Y, after.Y, t) - hazard.Center.Y) > 2) continue;
            hazard.HitMask |= bit;
            if (!AcceptHit(racer)) continue;
            racer.Speed *= .76f;
            racer.Slowed = Math.Max(racer.Slowed, .85f);
            Wave(after + Vector3.Up * .25f, hazard.Kind.Tint, 1.55f, .46f);
            if (racer == Player) Callout(hazard.Kind.Name + "!", 1.05f);
        }
    }
}
