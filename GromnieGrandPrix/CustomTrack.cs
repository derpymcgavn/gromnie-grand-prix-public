using Godot;
using DerethSurvivors;
using System.Text.Json.Serialization;

internal sealed record CustomTrack(int Id, string Name, uint Center, int GridSide, float Width, float[][] Points, int Version = 1,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TrackTravelMode[]? Modes = null)
{
    internal const int FirstId = 20_000;
    internal const int PreviewId = -1;
    internal const int MaxPoints = 256;
    internal const int DefaultWidth = 32, MinWidth = 16, MaxWidth = 64;
    internal static readonly int[] Grids = [1, 3, 7, 8, 14];

    internal bool IsValid => Version == 1 && (Id >= FirstId || Id == PreviewId) &&
        !string.IsNullOrWhiteSpace(Name) && Name.Length <= 42 && !Name.Any(char.IsControl) &&
        (Center & 0xffff) == 0xffff && Grids.Contains(GridSide) &&
        float.IsFinite(Width) && Width is >= MinWidth and <= MaxWidth &&
        Points is { Length: >= 4 and <= MaxPoints } &&
        Points.All(point => point is { Length: 2 } && point.All(float.IsFinite)) &&
        (Modes is null || Modes.Length == Points.Length && Modes.All(mode => Enum.IsDefined(mode)));

    internal Vector2[] Controls() => Points.Select(point => new Vector2(point[0], point[1])).ToArray();

    internal BogCircuit Build(AcLandArena land)
    {
        if (!IsValid || land.Block.Id != Center || land.GridSide != GridSide)
            throw new InvalidDataException("Invalid custom track layout.");
        var points = Controls();
        if (points.Any(point => !land.WorldBounds.HasPoint(point)) ||
            points.Where((point, i) => point.DistanceSquaredTo(points[(i + 1) % points.Length]) < 1).Any())
            throw new InvalidDataException("Keep the spline points inside the grid and at least 1 m apart.");
        if (ControlLength(points, true) > 39000)
            throw new InvalidDataException("This layout is too long. A custom lap can be up to 26 km.");
        var route = new BogCircuit(Name, points, land, Width * .5f, gradedRoad: true, custom: true, modes: Modes);
        try
        {
            ValidateRoute(route);
            return route;
        }
        catch
        {
            route.Curve.Dispose();
            throw;
        }
    }

    internal static float ControlLength(IReadOnlyList<Vector2> points, bool closed)
    {
        float length = 0;
        for (int i = 1; i < points.Count; i++) length += points[i].DistanceTo(points[i - 1]);
        if (closed && points.Count > 1) length += points[0].DistanceTo(points[^1]);
        return length;
    }

    private static void ValidateRoute(BogCircuit route)
    {
        if (route.Length is < 150 or > 26000)
            throw new InvalidDataException("A custom lap must be between 150 m and 26 km.");
        if (route.TightestInsideRadius() < 1)
            throw new InvalidDataException("A bend pinches the road. Spread those points apart or reduce the road width.");
        foreach (var section in route.TravelSections)
        {
            float length = section.End - section.Start;
            if (section.Mode == TrackTravelMode.JetSki && length < 38)
                throw new InvalidDataException("A Jet Ski section needs at least 38 m. Spread its mode changes apart.");
            if (section.Mode == TrackTravelMode.Flight && length < 58)
                throw new InvalidDataException("A Flight section needs at least 58 m for takeoff and landing.");
        }
        Rect2 edge = route.NativeBounds.Grow(-2);
        int count = Mathf.CeilToInt(route.Length / 4);
        var visited = new Dictionary<Vector2I, List<(Vector2 From, Vector2 To, int Index)>>();
        Vector2 previous = Flat(route.Curve.SampleBaked(0, true));
        float[] lanes = [-route.HalfWidth, 0, route.HalfWidth];
        for (int i = 1; i <= count; i++)
        {
            float at = i * route.Length / count;
            Vector2 point = Flat(route.Curve.SampleBaked(at, true));
            var near = new Rect2(previous, Vector2.Zero).Expand(point);
            Vector2I first = Cell(near.Position), last = Cell(near.End);
            for (int y = first.Y; y <= last.Y; y++)
                for (int x = first.X; x <= last.X; x++)
                {
                    var cell = new Vector2I(x, y);
                    if (!visited.TryGetValue(cell, out var segments)) visited[cell] = segments = [];
                    foreach (var segment in segments)
                        if (i - segment.Index > 1 && !(segment.Index == 1 && i == count) &&
                            Geometry2D.SegmentIntersectsSegment(previous, point, segment.From, segment.To).VariantType != Variant.Type.Nil)
                            throw new InvalidDataException($"The road crosses itself at {at:0} m. Move the nearby spline points.");
                    segments.Add((previous, point, i));
                }
            previous = point;
            TrackTravelMode mode = route.TravelMode(at);

            foreach (float lane in lanes)
            {
                var surface = route.Surface(at, lane);
                if (!edge.HasPoint(Flat(surface)))
                    throw new InvalidDataException($"The route runs outside the loaded grid near {at:0} m.");
                if (mode == TrackTravelMode.JetSki && !route.NavigableWater(surface) &&
                    (!route.OnWaterApproach(at, mode) || route.FreeGround(surface).Y > route.WaterCourseLevel!.Value + 2.3f))
                    throw new InvalidDataException($"The Jet Ski channel needs open water across its width near {at:0} m. Move the spline or narrow the course.");
                if (mode == TrackTravelMode.Ground && !route.NativeDry(surface) &&
                    !route.OnWaterApproach(at, mode) && surface.Y - route.FreeGround(surface).Y < 1.9f)
                    throw new InvalidDataException($"The road dips into water near {at:0} m. Move this section onto land.");
            }
            if (mode == TrackTravelMode.Ground && route.TravelMode(at + 3) == TrackTravelMode.Ground &&
                Math.Abs(route.Point(at + 3).Y - route.Point(at).Y) > 2.35f)
                throw new InvalidDataException($"The grade is too steep near {at:0} m. Give that climb more room.");
        }
    }

    internal static void ValidateScenery(BogCircuit route, AcLandArena land)
    {
        for (float at = 0; at < route.Length; at += 3)
        {
            if (route.TravelMode(at) == TrackTravelMode.Flight) continue;
            var point = route.Point(at);
            var raised = new System.Numerics.Vector3(point.X, point.Y + 1.22f, point.Z);
            if (land.Physics.Trace(raised, raised, route.HalfWidth + 1.2f,
                includeTerrain: false, stepTop: point.Y + .22f) is not null)
                throw new InvalidDataException($"Native scenery blocks the road near {at:0} m. Move the route around it.");
        }
    }

    private static Vector2 Flat(Vector3 point) => new(point.X, point.Z);
    private static Vector2I Cell(Vector2 point) => new(Mathf.FloorToInt(point.X / 32), Mathf.FloorToInt(point.Y / 32));
}
