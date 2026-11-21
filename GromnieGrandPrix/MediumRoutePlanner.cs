using Godot;
using DerethSurvivors;

internal sealed class MediumRoutePlanner
{
    private const float Width = 12, SampleStep = 6;
    private readonly AcLandArena land;
    private readonly Vector2 origin;
    private readonly int across;
    private readonly float[] terrain, water, obstacles;

    private MediumRoutePlanner(AcLandArena land)
    {
        this.land = land;
        origin = land.WorldBounds.Position;
        across = (int)(land.WorldBounds.Size.X / SampleStep) + 1;
        terrain = new float[across * across];
        water = new float[terrain.Length];
        obstacles = new float[terrain.Length];
        for (int z = 0; z < across; z++)
        for (int x = 0; x < across; x++)
        {
            Vector2 p = origin + new Vector2(x, z) * SampleStep;
            terrain[z * across + x] = land.Ground(p.X, p.Y).Y;
            water[z * across + x] = TownRoutePlanner.Dry(land, p.X, p.Y) ? 0 : 1;
        }
        foreach (var prop in land.CollisionObjects)
        {
            Vector2 p = (new Vector2(prop.Position.X, prop.Position.Z) - origin) / SampleStep;
            float reach = (Math.Max(prop.VisualRadius, prop.NativeRadius) + Width + 11) / SampleStep;
            for (int z = Math.Max(0, Mathf.FloorToInt(p.Y - reach)); z <= Math.Min(across - 1, Mathf.CeilToInt(p.Y + reach)); z++)
            for (int x = Math.Max(0, Mathf.FloorToInt(p.X - reach)); x <= Math.Min(across - 1, Mathf.CeilToInt(p.X + reach)); x++)
            {
                float near = Math.Max(0, reach - p.DistanceTo(new Vector2(x, z))) * SampleStep;
                obstacles[z * across + x] = Math.Max(obstacles[z * across + x], near);
            }
        }
    }

    internal static uint[] FindCenters(uint town, DatArchive cells, Dictionary<uint, float> wetness)
    {
        int bx = (int)(town >> 24), by = (int)((town >> 16) & 255);
        var candidates = new List<(uint Center, float Score)>();
        for (int dx = -2; dx <= 2; dx++)
        for (int dy = -2; dy <= 2; dy++)
        {
            int x = bx + dx, y = by + dy;
            if (x is < 3 or > 252 || y is < 3 or > 252) continue;
            uint id = ((uint)x << 24) | ((uint)y << 16) | 0xffff;
            uint[] neighbors = AcLandArena.NeighborIds(id, MediumRoute.Grid);
            if (!neighbors.All(cells.Entries.ContainsKey)) continue;
            float score = (Math.Abs(dx) + Math.Abs(dy)) * 1.7f;
            foreach (uint block in neighbors)
            {
                if (!wetness.TryGetValue(block, out float wet))
                {
                    var data = AcWorldData.ReadBlock(cells.Read(block));
                    wetness[block] = wet = data.Terrain.Count(tile => ((tile >> 2) & 31) is >= 16 and <= 20 or 22 or 23) / 81f;
                }
                score += wet;
            }
            candidates.Add((id, score));
        }
        return candidates.OrderBy(candidate => candidate.Score).Select(candidate => candidate.Center).ToArray();
    }

    public static MediumRoute Plan(PointOfInterestCatalog.Destination town, AcLandArena land,
        IReadOnlyList<XlRoute> templates, out MediumCourseStats stats)
    {
        var planner = new MediumRoutePlanner(land);
        return planner.Plan(town, templates, out stats);
    }

    private MediumRoute Plan(PointOfInterestCatalog.Destination town, IReadOnlyList<XlRoute> templates, out MediumCourseStats stats)
    {
        var arrival = XlRoute.TownPosition(town, land);
        var candidates = new List<(Vector2[] Points, float Score)>();
        Vector2 center = land.WorldBounds.GetCenter();
        foreach (var template in templates)
        {
            var original = template.Points();
            Vector2 low = original.Aggregate((a, b) => a.Min(b));
            Vector2 high = original.Aggregate((a, b) => a.Max(b));
            var sourceCenter = (low + high) * .5f;
            for (int turn = 0; turn < 8; turn++)
            foreach (var offset in new[] { Vector2.Zero, new Vector2(42, 0), new Vector2(-42, 0), new Vector2(0, 42), new Vector2(0, -42) })
            {
                float angle = (turn % 4) * Mathf.Pi * .5f;
                var points = original.Select(p => ((p - sourceCenter) * new Vector2(turn < 4 ? .78f : -.78f, .78f))
                    .Rotated(angle) + center + offset).ToArray();
                float score = 0, townGap = float.PositiveInfinity;
                for (int i = 0; i < points.Length; i++)
                {
                    score += SpanCost(points, i, 4);
                    townGap = Math.Min(townGap, Sample(points, i, 0).Position.DistanceTo(arrival));
                    townGap = Math.Min(townGap, Sample(points, i, .5f).Position.DistanceTo(arrival));
                }
                candidates.Add((points, score / points.Length + townGap * 1.45f + Math.Max(0, townGap - 146) * 10));
            }
        }

        MediumRoute? best = null;
        MediumCourseStats bestStats = default;
        int checkedRoutes = 0;
        foreach (var candidate in candidates.OrderBy(c => c.Score).Take(28))
        {
            var points = candidate.Points;
            Refine(points);
            int start = Enumerable.Range(0, points.Length).MinBy(i =>
            {
                var (p, direction, acceleration) = Sample(points, i, .28f);
                return p.DistanceTo(arrival) + (Field(water, p) * 60) + Curvature(direction, acceleration) * 1800;
            });
            var route = new MediumRoute(town.Id, land.Block.Id, Width,
                points.Skip(start).Concat(points.Take(start)).Select(p => new[] { MathF.Round(p.X, 2), MathF.Round(p.Y, 2) }).ToArray());
            var track = route.Build(town.Name, land);
            using var curve = track.Curve;
            if (!MediumRoute.Measure(track, land, arrival, out var measured)) continue;
            if (best is null || measured.WetMetres < bestStats.WetMetres) (best, bestStats) = (route, measured);
            if (bestStats.WetMetres < 750 || ++checkedRoutes >= 5) break;
        }
        stats = bestStats;
        return best ?? throw new InvalidDataException($"No checked medium circuit around {town.Name}.");
    }

    private void Refine(Vector2[] points)
    {
        var initial = points.ToArray();
        int count = points.Length;
        foreach (float step in new[] { 44f, 26f, 16f, 9f, 5f, 2.5f })
        for (int sweep = 0; sweep < 2; sweep++)
        for (int k = 0; k < count; k++)
        {
            int i = sweep == 0 ? k : count - 1 - k;
            float Cost() => SpanCost(points, i - 2, 7) + SpanCost(points, i - 1, 7)
                + SpanCost(points, i, 7) + SpanCost(points, i + 1, 7) + .035f * points[i].DistanceSquaredTo(initial[i]);
            Vector2 old = points[i], best = old;
            float score = Cost();
            for (int dir = 0; dir < 8; dir++)
            {
                points[i] = old + Vector2.Right.Rotated(dir * Mathf.Pi / 4) * step;
                float cost = Cost();
                if (cost < score) (best, score) = (points[i], cost);
            }
            points[i] = best;
        }
    }

    private float SpanCost(Vector2[] points, int span, int samples)
    {
        float cost = 0;
        for (int i = 0; i < samples; i++)
        {
            var (p, tangent, turn) = Sample(points, span, (float)i / samples);
            Vector2 forward = tangent.Normalized();
            Vector2 side = new(-forward.Y, forward.X);
            Vector2 a = p + side * Width, b = p - side * Width;
            var toEdge = (p - origin).Min(land.WorldBounds.End - p);
            float room = Math.Min(toEdge.X, toEdge.Y);
            float rise = Math.Abs(Field(terrain, p + forward * 7) - Field(terrain, p - forward * 7)) / 14;
            float camber = Math.Abs(Field(terrain, a) - Field(terrain, b)) / (2 * Width);
            cost += 240 * Field(water, p) + 24 * (Field(water, a) + Field(water, b));
            cost += 14 * Squared(Field(obstacles, p)) + 3 * Squared(Math.Max(0, 34 - room));
            cost += 760 * Squared(Math.Max(0, rise - .27f)) + 400 * Squared(Math.Max(0, camber - .32f));
            cost += 4200 * Squared(Math.Max(0, Curvature(tangent, turn) * (Width + 43) - 1));
            cost += 8 * Squared(Math.Max(0, 38 - tangent.Length()));
        }
        return cost / samples;
    }

    private float Field(float[] grid, Vector2 point)
    {
        Vector2 p = ((point - origin) / SampleStep).Clamp(Vector2.Zero, Vector2.One * (across - 1.001f));
        int x = (int)p.X, z = (int)p.Y, i = z * across + x;
        return Mathf.Lerp(Mathf.Lerp(grid[i], grid[i + 1], p.X - x),
            Mathf.Lerp(grid[i + across], grid[i + across + 1], p.X - x), p.Y - z);
    }

    private static (Vector2 Position, Vector2 Tangent, Vector2 Turn) Sample(Vector2[] points, int span, float t)
    {
        int n = points.Length, j = (span + n) % n;
        Vector2 a = points[(j + n - 1) % n], b = points[j], c = points[(j + 1) % n], d = points[(j + 2) % n];
        Vector2 first = (c - a) * .5f, second = (a - 2 * b + c) * .5f, third = (-a + 3 * b - 3 * c + d) / 6;
        return ((a + 4 * b + c) / 6 + t * (first + t * (second + t * third)),
            first + t * (2 * second + 3 * t * third), 2 * second + 6 * t * third);
    }

    private static float Curvature(Vector2 tangent, Vector2 turn) =>
        Math.Abs(tangent.Cross(turn)) / Math.Max(1, tangent.LengthSquared() * tangent.Length());

    private static float Squared(float value) => value * value;
}
