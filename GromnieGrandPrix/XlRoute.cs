using Godot;
using DerethSurvivors;

internal sealed record XlRoute(int TownId, uint Center, float HalfWidth, float[][] Waypoints)
{
    public Vector2[] Points() => Waypoints.Select(p => new Vector2(p[0], p[1])).ToArray();

    public static Vector2 TownPosition(PointOfInterestCatalog.Destination town, AcLandArena land) => new(
        ((int)(town.Landblock >> 24) - (int)(land.Block.Id >> 24)) * AcLandArena.BlockSize + town.X - land.CenterX,
        ((int)((land.Block.Id >> 16) & 255) - (int)((town.Landblock >> 16) & 255)) * AcLandArena.BlockSize + land.CenterY - town.Y);

    public static bool Validate(BogCircuit track, AcLandArena land, Vector2 town, out XlCourseStats stats)
    {
        stats = default;
        if (track.Length is < 5800 or > 8500 || land.Blocks.Count != 64) return false;

        var blocks = new HashSet<uint>();
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        float townGap = float.PositiveInfinity;
        Vector3 previous = track.NativeSurface(0, 0);
        for (float at = 0; at < track.Length; at += 3)
        {
            // Town-route limits describe the native corridor that was selected by the planner.
            Vector3 p = track.NativeSurface(at, 0);
            var flat = new Vector2(p.X, p.Z);
            Vector3 right = track.Right(at);
            if (Math.Abs(p.Y - previous.Y) > 2.55f ||
                track.TurnRadius(at) - track.HalfWidth < 37 ||
                !TownRoutePlanner.Clear(land, p, track.HalfWidth + 1.4f) ||
                !CrossSection(land, flat, new Vector2(right.X, right.Z), p.Y, track.HalfWidth))
                return false;
            min = min.Min(flat);
            max = max.Max(flat);
            townGap = Math.Min(townGap, flat.DistanceTo(town));
            blocks.Add(land.Resolve(p.X, p.Z).Block.Id);
            previous = p;
        }

        int columns = blocks.Select(id => id >> 24).Distinct().Count();
        int rows = blocks.Select(id => (id >> 16) & 255).Distinct().Count();
        uint minColumn = land.Blocks.Keys.Min(id => id >> 24);
        uint minRow = land.Blocks.Keys.Min(id => (id >> 16) & 255);
        int interior = blocks.Count(id => ((id >> 24) - minColumn) is > 0 and < 7 &&
            (((id >> 16) & 255) - minRow) is > 0 and < 7);
        stats = new(track.Length, max - min, rows, columns, blocks.Count, interior, townGap);
        return rows == 8 && columns == 8 && blocks.Count >= 40 && interior >= 20 && townGap < 155 &&
            stats.Span.X >= 1120 && stats.Span.Y >= 1120;
    }

    private static bool CrossSection(AcLandArena land, Vector2 p, Vector2 side, float height, float width)
    {
        var a = p - side * width;
        var b = p + side * width;
        return TownRoutePlanner.Dry(land, a.X, a.Y) && TownRoutePlanner.Dry(land, b.X, b.Y) &&
            Math.Abs(land.Ground(a.X, a.Y).Y - height) <= width * .85f &&
            Math.Abs(land.Ground(b.X, b.Y).Y - height) <= width * .85f;
    }
}

internal readonly record struct XlCourseStats(float Length, Vector2 Span, int Rows, int Columns,
    int VisitedBlocks, int InteriorBlocks, float TownGap);
