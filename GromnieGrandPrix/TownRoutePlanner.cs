using Godot;
using DerethSurvivors;
using Json = System.Text.Json.JsonSerializer;

internal sealed record TownRoute(int Id, float HalfWidth, float[][] Waypoints, bool Compact = false);

internal static class TownRoutePlanner
{
    private const int Sectors = 16;
    private static readonly float[] Radii = [34, 48, 62, 76, 90, 106, 122, 140, 158, 180, 204];
    private static readonly Vector2[] Centers =
    [
        Vector2.Zero, new(26, 0), new(-26, 0), new(0, 26), new(0, -26),
        new(29, 29), new(-29, 29), new(29, -29), new(-29, -29),
        new(56, 0), new(-56, 0), new(0, 56), new(0, -56),
        new(55, 55), new(55, -55), new(-55, 55), new(-55, -55)
    ];

    public static TownRoute Plan(PointOfInterestCatalog.Destination town, AcLandArena land, TownRoute? previous, bool compactOnly = false, bool gentle = false)
    {
        Vector2 arrival = new(town.X - land.CenterX, land.CenterY - town.Y);
        var buildings = land.CollisionObjects.Where(obj => obj.IsBuilding)
            .Select(obj => new Vector2(obj.Position.X, obj.Position.Z))
            .Where(p => p.DistanceTo(arrival) < 155)
            .OrderBy(p => p.DistanceSquaredTo(arrival))
            .Take(12).ToArray();
        Vector2 anchor = buildings.Length > 0
            ? buildings.Aggregate(Vector2.Zero, (sum, p) => sum + p) / buildings.Length : arrival;
        var origins = Centers.Select(offset => anchor + offset)
            .Concat(CandidateCenters().Select(offset => arrival + offset)).Distinct()
            .Where(center => Math.Abs(center.X) < 220 && Math.Abs(center.Y) < 220).ToArray();
        // Run the cheap swept oval candidates for each width first. Obstacle-heavy
        // towns can then try a gently deforming loop around the same landblocks.
        float[] widths = compactOnly ? [] : gentle ? [15] : [15, 13, 11, 9.5f, 8, 6.5f, 5.25f, 4.25f];
        foreach (float width in widths)
        {
            foreach (Vector2 center in origins)
            {
                foreach (float radius in new[] { 146f, 124f, 168f, 104f, 84f, 66f, 52f, 42f })
                    foreach (float aspect in new[] { 1f, .78f })
                        foreach (float yaw in aspect == 1 ? new[] { 0f } : new[] { 0f, Mathf.Pi / 4, Mathf.Pi / 2, Mathf.Pi * .75f })
                        {
                            if (radius * aspect * aspect < width + BogCircuit.MinimumInsideRadius + 1) continue;
                            var oval = Enumerable.Range(0, Sectors).Select(i =>
                            {
                                float angle = i * Mathf.Tau / Sectors;
                                return center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * aspect).Rotated(yaw);
                            }).ToArray();
                            var route = MakeRoute(town, land, oval, anchor, width, gentle: gentle);
                            if (route is not null) return route;
                        }
            }
            // Save the deforming search for reasonable widths on rough sites.
            if (width > 11) continue;
            foreach (Vector2 center in origins.Take(55))
                foreach (float rotation in new[] { 0f, Mathf.Pi / Sectors })
                {
                    var candidate = MakeLoop(land, center, width, rotation);
                    if (candidate is null) continue;
                    var route = MakeRoute(town, land, candidate, anchor, width);
                    if (route is not null) return route;
                }
        }
        return (gentle ? null : MakeCompactRoute(town, land, previous, anchor))
            ?? throw new InvalidDataException($"No checked loop around {town.Name}.");
    }

    private static TownRoute? MakeCompactRoute(PointOfInterestCatalog.Destination town, AcLandArena land,
        TownRoute? previous, Vector2 anchor)
    {
        if (previous is null) return null;
        Vector2[] seed = Points(previous);
        Vector2 origin = seed.Aggregate(Vector2.Zero, (sum, point) => sum + point) / seed.Length;
        var centers = Centers.Select(shift => origin + shift * .40f).ToArray();
        foreach (float width in new[] { 12f, 10f, 8f, 6.5f, 5.25f, 4.25f, 3.75f })
        {
            foreach (Vector2 center in centers)
            {
                foreach (float scale in new[] { 1.14f, 1f, .93f, 1.25f })
                {
                    var controls = seed.Select(point => center + (point - origin) * scale).ToArray();
                    var route = MakeRoute(town, land, controls, anchor, width, compact: true);
                    if (route is not null) return route;
                }
                foreach (float radius in new[] { 49f, 43f, 37f, 33f, 29f, 26f, 23f })
                {
                    if (radius < width + 16) continue;
                    var circle = Enumerable.Range(0, Sectors).Select(i =>
                    {
                        float angle = i * Mathf.Tau / Sectors;
                        return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    }).ToArray();
                    var route = MakeRoute(town, land, circle, anchor, width, compact: true);
                    if (route is not null) return route;
                }
            }
        }
        return null;
    }
    private static TownRoute? MakeRoute(PointOfInterestCatalog.Destination town, AcLandArena land, Vector2[] controls,
        Vector2 anchor, float width, bool compact = false, bool gentle = false)
    {
        var preliminary = new BogCircuit(town.Name, controls, land, width);
        using var preliminaryCurve = preliminary.Curve;
        if (!Validate(preliminary, land, width, compact, quick: true, gentle: gentle)) return null;
        int start = StartSector(controls, anchor, land);
        var ordered = controls.Skip(start).Concat(controls.Take(start)).ToArray();
        var track = new BogCircuit(town.Name, ordered, land, width);
        using var routeCurve = track.Curve;
        return Validate(track, land, width, compact, gentle: gentle) ? new TownRoute(town.Id, width, ordered.Select(p => new[] { p.X, p.Y }).ToArray(), compact) : null;
    }
    private static IEnumerable<Vector2> CandidateCenters()
    {
        foreach (var center in Centers) yield return center;
        foreach (float radius in new[] { 85f, 112f, 145f, 180f, 212f, 250f })
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.Tau / 16;
                yield return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }
    }
    public static Vector2[] Points(TownRoute route) => route.Waypoints.Select(p => new Vector2(p[0], p[1])).ToArray();
    private static int StartSector(Vector2[] points, Vector2 town, AcLandArena land)
    {
        float bestScore = float.MaxValue;
        int best = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length];
            Vector2 c = points[(i + 2) % points.Length];
            float corner = 1 - (b - a).Normalized().Dot((c - b).Normalized());
            Vector2 middle = a.Lerp(b, .55f);
            float grade = Math.Abs(land.Ground(a.X, a.Y).Y - land.Ground(b.X, b.Y).Y);
            float score = middle.DistanceTo(town) + corner * 65 + grade * 4 + (IsNativeRoad(land, middle) ? -25 : 0);
            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }
        return best;
    }

    private static Vector2[]? MakeLoop(AcLandArena land, Vector2 center, float width, float rotation)
    {
        int count = Radii.Length;
        var points = new Vector2[Sectors, count];
        var edges = new float[Sectors, count, count];
        for (int sector = 0; sector < Sectors; sector++)
        {
            float angle = sector * Mathf.Tau / Sectors + rotation;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            for (int i = 0; i < count; i++) points[sector, i] = center + direction * Radii[i];
        }

        for (int sector = 0; sector < Sectors; sector++)
            for (int i = 0; i < count; i++)
                for (int j = 0; j < count; j++)
                {
                    Vector2 a = points[sector, i], b = points[(sector + 1) % Sectors, j];
                    edges[sector, i, j] = Math.Abs(j - i) > 1 ? float.PositiveInfinity : SegmentCost(land, a, b, width)
                        + Math.Abs(j - i) * 8 + Math.Abs(i - 7) * 1.15f;
                }

        float bestCost = float.PositiveInfinity;
        Vector2[]? best = null;
        for (int start = 0; start < count; start++)
        {
            var back = new int[Sectors, count];
            var costs = Enumerable.Repeat(float.PositiveInfinity, count).ToArray();
            costs[start] = 0;
            for (int k = 0; k < Sectors - 1; k++)
            {
                var next = Enumerable.Repeat(float.PositiveInfinity, count).ToArray();
                for (int i = 0; i < count; i++)
                    for (int j = 0; j < count; j++)
                    {
                        float cost = costs[i] + edges[k, i, j];
                        if (cost >= next[j]) continue;
                        next[j] = cost;
                        back[k + 1, j] = i;
                    }
                costs = next;
            }
            for (int end = 0; end < count; end++)
            {
                float cost = costs[end] + edges[Sectors - 1, end, start];
                if (cost >= bestCost) continue;
                bestCost = cost;
                best = new Vector2[Sectors];
                int index = end;
                for (int k = Sectors - 1; k >= 0; k--)
                {
                    best[k] = points[k, index];
                    index = back[k, index];
                }
            }
        }
        return best;
    }

    private static float SegmentCost(AcLandArena land, Vector2 a, Vector2 b, float width)
    {
        float span = a.DistanceTo(b);
        int count = Mathf.CeilToInt(span / 4f);
        float score = 0, prev = land.Ground(a.X, a.Y).Y;
        for (int n = 0; n <= count; n++)
        {
            Vector2 point = a.Lerp(b, (float)n / count);
            Vector3 ground = land.Ground(point.X, point.Y);
            float rise = Math.Abs(ground.Y - prev);
            if (rise > span / count * .66f || !Clear(land, ground, width + 1.6f))
                return float.PositiveInfinity;
            prev = ground.Y;
            score += rise * 2.4f + (IsNativeRoad(land, point) ? .3f : 3);
        }
        return score * 8 / (count + 1);
    }

    public static bool Validate(BogCircuit track, AcLandArena land, float width, bool compact = false, bool quick = false, bool gentle = false)
    {
                if (track.Length < (compact ? 133 : 218) || track.Length > 1320) return false;
        float minimumRadius = compact ? 16f : BogCircuit.MinimumInsideRadius;
        float step = quick ? 14f : 1.5f;
        float forwardGrade = gentle ? .39f : .67f;
        float sideGrade = gentle ? .38f : .84f;
        float prev = track.Point(0).Y;
        for (float at = 0; at < track.Length; at += step)
        {
            Vector3 point = track.Point(at);
            if (Math.Abs(point.Y - prev) > step * forwardGrade || track.TurnRadius(at) - width < minimumRadius || !Clear(land, point, width + 1.35f)) return false;
            Vector3 left = track.Surface(at, -width), right = track.Surface(at, width);
            if (!Dry(land, left.X, left.Z) || !Dry(land, right.X, right.Z)
                || Math.Abs(left.Y - point.Y) > width * sideGrade || Math.Abs(right.Y - point.Y) > width * sideGrade) return false;
            prev = point.Y;
        }
        return true;
    }

    internal static bool Clear(AcLandArena land, Vector3 point, float margin)
    {
        if (!land.WorldBounds.Grow(-margin - 3).HasPoint(new(point.X, point.Z))) return false;
        var p = new System.Numerics.Vector3(point.X, point.Y + 1.22f, point.Z);
        if (land.Physics.Trace(p, p, margin, includeTerrain: false, stepTop: point.Y + .20f) is not null)
            return false;
        return Dry(land, point.X, point.Z) && Dry(land, point.X - margin, point.Z) && Dry(land, point.X + margin, point.Z)
            && Dry(land, point.X, point.Z - margin) && Dry(land, point.X, point.Z + margin);
    }

    internal static bool Dry(AcLandArena land, float x, float z)
    {
        var p = land.Resolve(x, z);
        int type = AcWorldData.TerrainAt(p.Block, p.X, p.Y);
        return type is not (>= 16 and <= 20 or 22 or 23);
    }

    private static bool IsNativeRoad(AcLandArena land, Vector2 point)
    {
        var p = land.Resolve(point.X, point.Y);
        return AcTerrainBlend.OnRoad(p.Block, p.X, p.Y);
    }

    public static void Write(string path, IReadOnlyList<TownRoute> routes) =>
        System.IO.File.WriteAllText(path, Json.Serialize(routes));
}








