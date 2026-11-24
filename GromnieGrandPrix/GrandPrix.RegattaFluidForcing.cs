using Godot;

public partial class GrandPrix
{
    private struct RegattaHullTrail
    {
        internal bool Active;
        internal Vector2 Position, Direction;
    }

    private readonly RegattaHullTrail[] regattaHullTrails = new RegattaHullTrail[RacerCount];

    private void ResetRegattaFluid()
    {
        regattaFluid.Clear();
        Array.Clear(regattaHullTrails);
    }

    private void TickRegattaFluid(float dt)
    {
        if (!track.HasWaterSections || dt <= 0) return;

        // Every source uses its previous physics pose before any racer moves this tick.
        for (int i = 0; i < racers.Count && i < regattaHullTrails.Length; i++)
            DisplaceRegattaWater(racers[i], ref regattaHullTrails[i]);
        regattaFluid.Step(dt);
    }

    private void DisplaceRegattaWater(MountedRacer racer, ref RegattaHullTrail trail)
    {
        float speed = racer.Speed;
        if (!racer.UsesJetSki || racer.MountUnavailable || racer.SkiAirborne ||
            racer.EjectTime > 0 || racer.ResetHold > 0 || racer.LeapHeight > .1f ||
            racer.SkiWaterContact < .06f || speed < 2.5f)
        {
            trail = default;
            return;
        }

        Vector3 point = racer.CoursePosition(track);
        if (!track.NavigableWater(point))
        {
            trail = default;
            return;
        }

        Vector2 position = new(point.X, point.Z);
        Vector3 heading = racer.Heading(track);
        Vector2 forward = new Vector2(heading.X, heading.Z).Normalized();
        RegattaHullTrail previous = trail;
        trail = new RegattaHullTrail { Active = true, Position = position, Direction = forward };
        if (!previous.Active) return;

        float distance = position.DistanceTo(previous.Position);
        if (distance is < .04f or > 8) return;

        // Fill the path between physics poses, including the gap between online snapshots.
        int samples = Mathf.CeilToInt(distance / (RegattaFluidField.CellSize * .82f));
        float travelTime = Math.Min(.12f, distance / speed);
        float effort = Mathf.SmoothStep(2.5f, 28, speed) * Mathf.Lerp(.72f, 1, Mathf.SmoothStep(25, 59, speed));
        float load = effort * Mathf.SmoothStep(.06f, .73f, racer.SkiWaterContact) * travelTime / samples;
        float bow = load * (1 - .65f * Mathf.SmoothStep(.10f, .29f, racer.SkiPitch));
        float stern = load * Mathf.Lerp(.22f, 1f, racer.SkiSternContact);
        float turn = Mathf.Clamp(racer.Lean, -1, 1) * (racer.GroundDrifting ? .45f : .24f);
        for (int i = 0; i < samples; i++)
        {
            float progress = (i + 1f) / samples;
            Vector2 center = previous.Position.Lerp(position, progress);
            Vector2 direction = previous.Direction.Lerp(forward, progress).Normalized();
            if (direction.LengthSquared() < .5f) direction = forward;
            Vector2 right = new(-direction.Y, direction.X);

            // Pressure at the bow pushes water outward. Lower the water under the keel
            // and send the displaced water out behind the immersed sides of the stern.
            Vector2 nose = center + direction * 1.75f;
            regattaFluid.Disturb(nose - right * .9f, 2.4f, bow * 4.4f);
            regattaFluid.Disturb(nose + right * .9f, 2.4f, bow * 4.4f);
            regattaFluid.Disturb(center - direction * .55f, 2.25f, -load * 4.6f);

            Vector2 tail = center - direction * 1.90f;
            regattaFluid.Disturb(tail - right * 1.2f, 2.55f, stern * 4.0f * (1 + turn));
            regattaFluid.Disturb(tail + right * 1.2f, 2.55f, stern * 4.0f * (1 - turn));
        }
    }
}
