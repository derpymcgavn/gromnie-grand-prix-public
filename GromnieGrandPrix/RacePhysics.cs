using Godot;

internal static class RacePhysics
{
    internal const float Gravity = 9.80665f;
    internal const float TrackFallRescueTime = 1.45f;
    internal static readonly Vector3 Acceleration = Vector3.Down * Gravity;

    internal static Vector3 BallisticPosition(Vector3 position, Vector3 velocity, float seconds) =>
        position + velocity * seconds + Acceleration * (.5f * seconds * seconds);

    internal static bool TryGroundContact(BogCircuit track, Vector3 from, Vector3 to, float clearance,
        bool includeRoad, out float fraction, out Vector3 contact)
    {
        fraction = 1;
        contact = to;
        float floor = Height(to);
        if (to.Y > floor) return false;
        if (from.Y > Height(from))
        {
            float low = 0, high = 1;
            for (int probe = 0; probe < 8; probe++)
            {
                float middle = (low + high) * .5f;
                Vector3 point = from.Lerp(to, middle);
                if (point.Y > Height(point)) low = middle;
                else high = middle;
            }
            fraction = high;
            contact = from.Lerp(to, high);
            floor = Height(contact);
        }
        contact.Y = floor;
        return true;

        float Height(Vector3 point) => (includeRoad ? track.GroundOrRoadHeight(point) : track.FreeGround(point).Y) + clearance;
    }
}
