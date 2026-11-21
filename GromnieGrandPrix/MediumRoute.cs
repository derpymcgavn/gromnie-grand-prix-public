using Godot;
using DerethSurvivors;

internal sealed record MediumRoute(int TownId, uint Center, float HalfWidth, float[][] Waypoints)
{
    public const int Grid = 7;
    public BogCircuit Build(string name, AcLandArena land) =>
        new(name, Waypoints.Select(p => new Vector2(p[0], p[1])).ToArray(), land, HalfWidth, gradedRoad: true);

    public static bool Measure(BogCircuit track, AcLandArena land, Vector2 town, out MediumCourseStats stats)
    {
        stats = default;
        if (land.GridSide != Grid || track.Length is < 4250 or > 5950 || track.HalfWidth < 11) return false;
        var visited = new HashSet<uint>();
        var low = Vector2.One * float.PositiveInfinity;
        var high = -low;
        float townGap = float.PositiveInfinity, minimumRadius = float.PositiveInfinity, wet = 0;
        Vector3 previous = track.Point(track.Length - 4);
        for (float at = 0; at < track.Length; at += 4)
        {
            Vector3 point = track.Point(at);
            var flat = new Vector2(point.X, point.Z);
            float radius = track.TurnRadius(at) - track.HalfWidth;
            var center = new System.Numerics.Vector3(point.X, point.Y + 1.2f, point.Z);
            if (!land.WorldBounds.Grow(-track.HalfWidth - 5).HasPoint(flat) || radius < 26 || Math.Abs(point.Y - previous.Y) > 4)
                return false;
            if (land.Physics.Trace(center, center, track.HalfWidth + 1.2f, includeTerrain: false, stepTop: point.Y + .2f) is not null &&
                !ClearRacerSpace(track, land, at))
                return false;
            if (!track.NativeDry(track.NativeSurface(at, 0))) wet += 4;
            visited.Add(land.Resolve(point.X, point.Z).Block.Id);
            low = low.Min(flat);
            high = high.Max(flat);
            townGap = Math.Min(townGap, flat.DistanceTo(town));
            minimumRadius = Math.Min(minimumRadius, radius);
            previous = point;
        }
        stats = new(track.Length, visited.Count, high - low, townGap, minimumRadius, wet);
        return visited.Count >= 24 && stats.Span.X >= 900 && stats.Span.Y >= 900 && townGap <= 190;
    }

    private static bool ClearRacerSpace(BogCircuit track, AcLandArena land, float station)
    {
        // A wide sphere can hit hillside scenery above or below a banked lane.
        // Recheck the actual space a mount occupies across that cross section.
        float edge = track.HalfWidth;
        int columns = Mathf.CeilToInt(edge * 2 / 1.55f);
        for (int column = 0; column <= columns; column++)
        {
            float lane = Mathf.Lerp(-edge, edge, column / (float)columns);
            Vector3 before = track.Surface(station - 2, lane), after = track.Surface(station + 2, lane);
            float step = Math.Min(before.Y, after.Y) + .25f;
            var from = new System.Numerics.Vector3(before.X, before.Y + 1.10f, before.Z);
            var to = new System.Numerics.Vector3(after.X, after.Y + 1.10f, after.Z);
            var head = System.Numerics.Vector3.UnitY * 1.3f;
            if (land.Physics.Trace(from, to, .92f, includeTerrain: false, stepTop: step) is not null ||
                land.Physics.Trace(from + head, to + head, .92f, includeTerrain: false, stepTop: step) is not null)
                return false;
        }
        return true;
    }
}

internal readonly record struct MediumCourseStats(float Length, int Blocks, Vector2 Span, float TownGap, float InsideRadius, float WetMetres);
