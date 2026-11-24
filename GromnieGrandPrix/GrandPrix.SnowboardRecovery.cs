using Godot;

public partial class GrandPrix
{
    private void RecoverSnowboard(MountedRacer racer)
    {
        float distance = racer.Distance;
        float station = Mathf.Clamp(distance, 0, track.Length);
        Vector3 center = track.Point(station);
        Vector3 position = racer.CoursePosition(track);
        Vector3 forward = track.Forward(station);
        Vector3 heading = racer.Heading(track);
        float width = track.WidthAt(station);
        float slideLimit = width + MountedRacer.SnowboardShoulder;
        if (!DrySnowboardContact(position, 1) || FlatGapSquared(position, center) > slideLimit * slideLimit)
        {
            (position, station) = FindSnowboardReturn(position, station);
            heading = track.Forward(station);
        }
        else if (Math.Abs(racer.Lane) > width * .82f)
        {
            heading = (forward - track.Right(station) * (.65f * Math.Sign(racer.Lane))).Normalized();
        }
        else if (heading.Dot(forward) < .2f)
        {
            heading = forward;
        }

        float momentum = racer.HasSnowboardPhysics ? racer.SnowboardVelocity.Length() : racer.Speed;
        racer.Land();
        racer.Reattach();
        ClearRacerPowers(racer);
        ResetRivalMotion(racer);
        Array.Clear(racer.DustContact);
        racer.EjectTime = racer.ResetHold = racer.FallTime = 0;
        racer.Slowed = racer.Frozen = racer.Boost = racer.Lean = 0;
        racer.SuperDriftUntil = 0;
        racer.GroundDrifting = false;
        racer.Stability = 100;
        racer.SwingTime = racer.GuardWindow = racer.GuardFlash = 0;
        racer.ItemGrace = Math.Max(racer.ItemGrace, .8f);
        racer.ResumeSnowboard(track, position, heading, Mathf.Clamp(momentum * .20f, 3, 6));
        // Recovery moves the board back onto snow without awarding race progress.
        racer.Distance = distance;
        racer.Draw(track, 0, true);
        if (racer == Player)
        {
            lateral = charge = 0;
            drifting = false;
            Callout("BACK ON YOUR BOARD!", 1.2f);
        }
    }

    private (Vector3 Position, float Station) FindSnowboardReturn(Vector3 position, float station)
    {
        Vector3 best = track.Point(station);
        float bestStation = station;
        float bestGap = float.PositiveInfinity;
        if (!position.IsFinite()) position = best;
        // The snowboard routes are built on dry native terrain. Check the local
        // cross-sections toward the finish so a recovery cannot skip to another turn.
        for (float at = station; at <= Math.Min(station + 12, track.Length); at += 3)
        {
            float inside = track.WidthAt(at) * .55f;
            for (int side = -1; side <= 1; side++)
            {
                Vector3 candidate = track.Surface(at, side * inside);
                if (!DrySnowboardContact(candidate, 3)) continue;
                float gap = FlatGapSquared(position, candidate);
                if (gap >= bestGap) continue;
                best = candidate;
                bestStation = at;
                bestGap = gap;
            }
        }
        return (best, bestStation);
    }

    private bool DrySnowboardContact(Vector3 point, float edgeMargin) => point.IsFinite() &&
        track.NativeBounds.Grow(-edgeMargin).HasPoint(new(point.X, point.Z)) && track.NativeDry(point);

    private static float FlatGapSquared(Vector3 from, Vector3 to) =>
        new Vector2(from.X - to.X, from.Z - to.Z).LengthSquared();
}
