using Godot;
using DerethSurvivors;

internal enum VoidCircuitStyle { Battle, Jester, Kaleidoscope }

public sealed partial class BogCircuit
{
    public const float RoadHalf = 14f;
    public const float Grid = 22;
    public Curve3D Curve { get; }
    public float Length => IsVoid ? voidLength : Curve.GetBakedLength();

    private static readonly Vector2[] Corners =
    [
        new(24, 105), new(28, 22), new(25, -70), new(-10, -129), new(-78, -148),
        new(-139, -108), new(-161, -35), new(-159, 39), new(-132, 105),
        new(-81, 141), new(-25, 143)
    ];

    public string Name { get; }
    public float HalfWidth { get; }
    public int NativeBlockCount => land?.Blocks.Count ?? 0;
    public bool IsXL => land?.GridSide >= 8;
    public bool IsMedium => land?.GridSide == 7;
    public bool IsSprint => sprint is not null;
    internal bool IsDismountSprint { get; }
    internal bool IsSnowboardCourse { get; }
    internal float? WaterCourseLevel { get; }
    internal bool IsWaterCourse { get; }
    internal bool IsPointToPoint => IsSprint || IsDismountSprint || IsSnowboardCourse;
    internal float DismountLine => IsDismountSprint ? Length * .5f : float.PositiveInfinity;
    internal bool IsCustom { get; }
    internal bool IsVoid { get; }
    internal bool IsJester => IsVoid && voidStyle == VoidCircuitStyle.Jester;
    internal bool IsKaleidoscope => IsVoid && voidStyle == VoidCircuitStyle.Kaleidoscope;
    internal bool IsVoidBattle => IsVoid && voidStyle == VoidCircuitStyle.Battle;
    internal bool HasGradedRoad => ridgeRoad is not null || IsVoid;
    public bool IsRegional => IsXL || IsMedium || IsPointToPoint || IsCustom || IsVoid || IsWaterCourse;
    public int GridSide => land?.GridSide ?? 0;
    public int GateCount => IsVoid ? 14 : IsSnowboardCourse ? Mathf.Clamp(Mathf.CeilToInt(Length / 145), 5, 20)
        : IsCustom ? Mathf.Clamp(Mathf.CeilToInt(Length / 250), 4, 20)
        : IsWaterCourse ? Mathf.Clamp(Mathf.CeilToInt(Length / 325), 12, 24) : IsSprint ? 20 : IsXL ? 16 : IsMedium ? 12 : 4;
    public int Laps => IsVoid ? IsVoidBattle ? 5 : 3 : IsRegional ? 1 : 3;
    public Rect2 NativeBounds => land?.WorldBounds ?? default;
    internal float NativeElevationOrigin => land?.ElevationOrigin ?? 0;
    private readonly AcLandArena? land;
    private readonly RidgeRoad? ridgeRoad;
    private readonly VoidRoadSurface? voidRoad;
    private readonly VoidCircuitStyle voidStyle;
    private readonly VoidFrame[]? voidFrames;
    private readonly float voidLength, voidFrameRate;
    private readonly SpanSprint? sprint;
    private readonly float spanHigh;
    private float[]? flightGates;
    private Vector3[]? projectionSamples;
    public float GateHalfWidth => HalfWidth + .62f;

    private readonly record struct VoidFrame(Vector3 Center, Vector3 Forward, Vector3 Right, float Curvature);

    public BogCircuit() : this("Muckwater Circuit", Corners, null, RoadHalf) { }

    internal BogCircuit(string name, Vector2[] corners, AcLandArena? land, float halfWidth, bool gradedRoad = false,
        SpanSprint? sprint = null, bool custom = false, Vector3[]? voidCorners = null,
        VoidCircuitStyle voidStyle = VoidCircuitStyle.Battle, bool dismountSprint = false, float? waterCourseLevel = null,
        TrackTravelMode[]? modes = null, bool snowboardCourse = false)
    {
        Name = name;
        HalfWidth = halfWidth;
        this.land = land;
        this.sprint = sprint;
        IsDismountSprint = dismountSprint;
        IsSnowboardCourse = snowboardCourse;
        WaterCourseLevel = waterCourseLevel;
        IsWaterCourse = waterCourseLevel.HasValue;
        IsCustom = custom;
        IsVoid = voidCorners is not null;
        this.voidStyle = voidStyle;
        Curve = voidCorners is null ? MakeSpline(corners, !IsPointToPoint, .35f) : MakeSpline(voidCorners, true, .35f);
        try
        {
            ConfigureTravel(modes);
            if (travelSections.Any(section => section.Mode == TrackTravelMode.JetSki))
                WaterCourseLevel = InferHybridWaterLevel();
        }
        catch
        {
            Curve.Dispose();
            throw;
        }
        if (IsVoid)
        {
            voidLength = Curve.GetBakedLength();
            // Subdivide each rendered road section so its bank and tangent stay accurate.
            const int framesPerSection = 8;
            int count = framesPerSection * Mathf.CeilToInt(voidLength / VoidRoadSurface.MeshStep);
            voidFrameRate = count / voidLength;
            var frames = new VoidFrame[count + 1];
            for (int i = 0; i < count; i++)
            {
                float station = i / (float)count * voidLength;
                Vector3 forward = Forward(station);
                float curvature = Curvature(station);
                Vector3 across = forward.Cross(Vector3.Up).Normalized();
                float bank = Mathf.Clamp(curvature * 37, -.24f, .24f);
                frames[i] = new VoidFrame(FlatPoint(station), forward, across.Rotated(forward, bank), curvature);
            }
            frames[^1] = frames[0];
            voidFrames = frames;
            voidRoad = new VoidRoadSurface(this);
        }
        if (gradedRoad && (travelSections.Length == 0 || travelSections.Any(section => section.Mode == TrackTravelMode.Ground)))
            ridgeRoad = new RidgeRoad(this);
        if (IsDismountSprint)
        {
            int half = GateCount / 2;
            float approach = DismountLine - 15;
            flightGates = Enumerable.Range(0, GateCount + 1).Select(i => i <= half
                ? Mathf.Lerp(Grid, approach, i / (float)half)
                : Mathf.Lerp(approach, Length - 8, (i - half) / (float)(GateCount - half))).ToArray();
        }
        if (IsSnowboardCourse) flightGates = LocateSnowboardCheckpoints();
        if (sprint is not null)
        {
            spanHigh = sprint.Deck.Max(row => row[1]) - land!.ElevationOrigin;
            float before = Project(new Vector3(sprint.West - 37, 0, sprint.RiverAxis), 0).Distance;
            float after = Project(new Vector3(sprint.East + 36, 0, sprint.RiverAxis), 0).Distance;
            flightGates = Enumerable.Range(0, GateCount + 1).Select(i => i switch
            {
                <= 9 => Mathf.Lerp(Grid, before, i / 9f),
                10 => (before + after) * .5f,
                _ => Mathf.Lerp(after, Length - 8, (i - 11) / 9f)
            }).ToArray();
        }
    }

    internal static Curve3D MakeSpline(IReadOnlyList<Vector2> corners, bool loop, float bakeInterval) =>
        MakeSpline(corners.Select(p => new Vector3(p.X, 0, p.Y)).ToArray(), loop, bakeInterval);

    private static Curve3D MakeSpline(IReadOnlyList<Vector3> corners, bool loop, float bakeInterval)
    {
        var curve = new Curve3D { BakeInterval = bakeInterval };
        int nodes = corners.Count;
        if (nodes < 2) return curve;
        int count = nodes + (loop ? 1 : 0);
        for (int i = 0; i < count; i++)
        {
            Vector3 center = corners[i % nodes];
            Vector3 previous = corners[(i + nodes - 1) % nodes];
            Vector3 next = corners[(i + 1) % nodes];
            if (!loop && i == 0) previous = 2 * center - next;
            if (!loop && i == count - 1) next = 2 * center - previous;
            // The Bézier representation of a uniform cubic B-spline.
            Vector3 point = (previous + 4 * center + next) / 6;
            Vector3 tangent = (next - previous) / 6;
            curve.AddPoint(point, -tangent, tangent);
        }
        return curve;
    }

    public const float MinimumInsideRadius = 21f;
    public float Curvature(float at)
    {
        if (voidFrames is { } frames)
        {
            var (index, blend) = VoidFrameSample(at);
            return Mathf.Lerp(frames[index].Curvature, frames[index + 1].Curvature, blend);
        }
        const float halfSpan = 2.6f;
        Vector3 across = Forward(at).Cross(Vector3.Up);
        if (IsVoid) across = across.Normalized();
        return (Forward(at + halfSpan) - Forward(at - halfSpan)).Dot(across) / (2 * halfSpan);
    }

    public float TurnRadius(float at)
    {
        float curvature = Math.Abs(Curvature(at));
        return curvature < .0001f ? float.PositiveInfinity : 1 / curvature;
    }

    public float TightestInsideRadius()
    {
        float minimum = float.PositiveInfinity;
        for (float at = 0; at < Length; at += 1.5f)
            minimum = Math.Min(minimum, TurnRadius(at) - HalfWidth);
        return minimum;
    }
    public float Gate(int index) => IsPointToPoint ? flightGates![Math.Clamp(index, 0, GateCount)]
        : flightGates is null ? Grid + index * Length / GateCount
        : flightGates[index % GateCount] + index / GateCount * Length;

    private float[] LocateSnowboardCheckpoints()
    {
        var gates = new float[GateCount + 1];
        gates[0] = Grid;
        gates[^1] = Length - 32;
        float spacing = (gates[^1] - Grid) / GateCount;
        float search = Math.Min(32, spacing * .28f);
        for (int i = 1; i < GateCount; i++)
        {
            float ideal = Grid + i * spacing;
            float best = ideal, bestScore = float.MaxValue;
            for (float offset = -search; offset <= search; offset += 2)
            {
                float station = ideal + offset;
                const float look = 11;
                float center = Surface(station, 0).Y;
                float descentIn = (center - Surface(station - look, 0).Y) / look;
                float descentOut = (Surface(station + look, 0).Y - center) / look;
                float edge = HalfWidth * .68f;
                float crossfall = Math.Abs(Surface(station, edge).Y - Surface(station, -edge).Y) / (2 * edge);
                float turn = Forward(station - look).AngleTo(Forward(station + look));
                float score = Math.Abs(offset) / search * .14f + Math.Abs(descentIn + descentOut) * .20f +
                    crossfall * 1.1f + turn * .66f + Math.Max(0, descentIn - descentOut) * .75f;
                if (score >= bestScore) continue;
                best = station;
                bestScore = score;
            }
            gates[i] = best;
        }
        return gates;
    }

    internal void ReserveMirrorSector(float entry, float exit)
    {
        // A mirror may skip distance, but the two sides must share a checkpoint sector.
        flightGates = Enumerable.Range(0, GateCount).Select(i => i == GateCount - 1
            ? exit + 38 : Mathf.Lerp(Grid, entry - 36, i / (GateCount - 2f))).ToArray();
    }

    internal void ReserveFlightSector(float start, float end)
    {
        if (IsVoid)
        {
            float touchdown = end + 38;
            flightGates = Enumerable.Range(0, GateCount).Select(i => i switch
            {
                0 => Grid,
                1 => start - 32,
                _ => Mathf.Lerp(touchdown, Length + Grid, (i - 2f) / (GateCount - 2))
            }).ToArray();
            return;
        }
        float approach = start - 26, excluded = end - start + 56;
        flightGates = Enumerable.Range(0, GateCount).Select(i =>
        {
            float at = Grid + i * (Length - excluded) / GateCount;
            return at <= approach ? at : at + excluded;
        }).ToArray();
    }

    public (float Distance, float Lane) Project(Vector3 position, float previous)
    {
        if (projectionSamples is null)
        {
            int count = Math.Max(2, Mathf.CeilToInt(Length / (IsPointToPoint ? 2.25f : .9f)));
            projectionSamples = Enumerable.Range(0, count + 1)
                .Select(i => FlatPoint(i * Length / count)).ToArray();
        }
        Vector3 point = IsVoid ? position : position with { Y = 0 };
        float best = float.MaxValue, distance = 0;
        for (int i = 0; i < projectionSamples.Length - 1; i++)
        {
            Vector3 start = projectionSamples[i], end = projectionSamples[i + 1];
            Vector3 direction = end - start;
            float t = Mathf.Clamp((point - start).Dot(direction) / Math.Max(direction.LengthSquared(), .0001f), 0, 1);
            float gap = (point - start - direction * t).LengthSquared();
            if (gap >= best) continue;
            best = gap;
            distance = (i + t) * Length / (projectionSamples.Length - 1);
        }
        if (!IsPointToPoint) distance += Mathf.Round((previous - distance) / Length) * Length;
        return (distance, (position - FlatPoint(distance)).Dot(Right(distance)));
    }

    public bool TryCrossGate(int index, Vector3 before, Vector3 after, out float fraction)
    {
        float at = Gate(index);
        Vector3 center = Point(at), forward = Forward(at);
        float entry = (before - center).Dot(forward), exit = (after - center).Dot(forward);
        fraction = 0;
        if (entry >= 0 || exit < 0) return false;
        fraction = -entry / (exit - entry);
        Vector3 crossing = before.Lerp(after, fraction);
        float lane = (crossing - center).Dot(Right(at));
        float ground = HasHybridTravel || HasGradedRoad || IsWaterCourse ? Surface(at, lane).Y : FreeGround(crossing).Y;
        float height = crossing.Y - ground;
        return Math.Abs(lane) <= WidthAt(at) + .62f &&
            (IsSnowboardCourse ? height is > -.9f and < 24 : Math.Abs(height) < 2);
    }

    public Vector3 FreeGround(Vector3 point) => land?.Ground(point.X, point.Z)
        ?? Scenery?.FreeGround(point) ?? new Vector3(point.X, 0, point.Z);

    internal float GroundOrRoadHeight(Vector3 point) => RoadHeight(point, FreeGround(point).Y);
    internal float HighestRoadIn(Aabb bounds)
    {
        if (IsWaterCourse) return WaterCourseLevel!.Value;
        if (voidRoad is not null) return voidRoad.HighestNear(bounds);
        float highest = ridgeRoad?.HighestRoadIn(bounds) ?? float.NegativeInfinity;
        if (sprint is not null && bounds.Position.X <= sprint.East && bounds.End.X >= sprint.West &&
            bounds.Position.Z < sprint.RiverAxis + 3.3f && bounds.End.Z > sprint.RiverAxis - 3.3f)
            highest = Math.Max(highest, spanHigh);
        return highest;
    }

    internal float RoadHeight(Vector3 point, float floor = float.NegativeInfinity)
    {
        if (WaterCourseLevel is { } water)
        {
            if (IsWaterCourse) return Math.Max(water, floor);
            if (NativeWet(point)) floor = Math.Max(water, floor);
        }
        if (voidRoad is not null) return Math.Max(floor, voidRoad.HeightAt(point));
        if (sprint is not null && Math.Abs(point.Z - sprint.RiverAxis) <= sprint.WidthAt(point) + .35f &&
            sprint.DeckHeight(point, land!) is { } span)
            floor = Math.Max(floor, span);
        float top = ridgeRoad?.RoadHeight(point, floor) ?? floor;
        return HasGradedRoad && float.IsNegativeInfinity(floor) && float.IsFinite(top) ? Math.Max(top, FreeGround(point).Y) : top;
    }

    internal bool DustSurface(Vector3 point, bool offRoad, out bool stone)
    {
        stone = false;
        if (land is null) return !offRoad || point.Y > MuckwaterLayout.WaterLevel;
        if (!TownRoutePlanner.Dry(land, point.X, point.Z)) return false;
        var location = land.Resolve(point.X, point.Z);
        stone = AcTerrainBlend.OnRoad(location.Block, location.X, location.Y);
        return true;
    }

    internal bool CanStageSpectator(Vector3 at)
    {
        if (IsVoid || IsWaterCourse) return false;
        if (land is not null) return land.ContainsGround(at.X, at.Z) && TownRoutePlanner.Clear(land, at, .62f);
        return Scenery?.DryMove(at + Vector3.Right * .75f, at) ??
            new Rect2(MuckwaterLayout.Origin, MuckwaterLayout.Extent).Grow(-2).HasPoint(new Vector2(at.X, at.Z));
    }

    public bool TryFreeMove(Vector3 start, Vector3 destination, out Vector3 grounded, bool jetSki = false)
    {
        if ((IsWaterCourse || jetSki) && WaterCourseLevel is { } water)
        {
            grounded = destination with { Y = water };
            return NavigableWater(grounded) && ClearMove(land!.Physics, start, grounded);
        }
        grounded = FreeGround(destination);
        if (land is null)
            return Scenery?.DryMove(start, grounded) ??
                new Rect2(MuckwaterLayout.Origin, MuckwaterLayout.Extent).HasPoint(MuckwaterLayout.Flat(destination));
        if (!land.WorldBounds.Grow(-2).HasPoint(new(destination.X, destination.Z)) || !land.ContainsGround(destination.X, destination.Z))
            return false;
        var p = land.Resolve(grounded.X, grounded.Z);
        int type = AcWorldData.TerrainAt(p.Block, p.X, p.Y);
        if (type is >= 16 and <= 20 or 22 or 23) return false;
        return ClearMove(land.Physics, start, grounded);
    }

    internal static bool ClearMove(ProjectileWorld world, Vector3 start, Vector3 end)
    {
        float distance = new Vector2(end.X - start.X, end.Z - start.Z).Length();
        if (Math.Abs(end.Y - start.Y) > .25f + distance * .92f) return false;
        var from = new System.Numerics.Vector3(start.X, start.Y + .85f, start.Z);
        var to = new System.Numerics.Vector3(end.X, end.Y + .85f, end.Z);
        var touch = world.Trace(from, to, .76f, includeTerrain: false, requireInward: true, stepTop: Math.Min(start.Y, end.Y) + .22f);
        return touch is null;
    }
    private (int Index, float Blend) VoidFrameSample(float distance)
    {
        float sample = Mathf.PosMod(distance, voidLength) * voidFrameRate;
        int index = Math.Min((int)sample, voidFrames!.Length - 2);
        return (index, sample - index);
    }

    private Vector3 FlatPoint(float distance)
    {
        if (voidFrames is { } frames)
        {
            var (index, blend) = VoidFrameSample(distance);
            return frames[index].Center.Lerp(frames[index + 1].Center, blend);
        }
        return Curve.SampleBaked(IsPointToPoint ? Mathf.Clamp(distance, 0, Length) : Mathf.PosMod(distance, Length), true);
    }
    public Vector3 Ground(Vector3 p) => land?.Ground(p.X, p.Z) ?? p;
    public Vector3 Point(float distance) => Surface(distance, 0);
    public Vector3 Forward(float distance)
    {
        if (voidFrames is { } frames)
        {
            var (index, blend) = VoidFrameSample(distance);
            return frames[index].Forward.Lerp(frames[index + 1].Forward, blend).Normalized();
        }
        if (IsPointToPoint) distance = Mathf.Clamp(distance, .55f, Length - .55f);
        return (FlatPoint(distance + .55f) - FlatPoint(distance - .55f)).Normalized();
    }
    public Vector3 Right(float distance)
    {
        if (voidFrames is { } frames)
        {
            var (index, blend) = VoidFrameSample(distance);
            return frames[index].Right.Lerp(frames[index + 1].Right, blend).Normalized();
        }
        return Forward(distance).Cross(Vector3.Up);
    }
    private Vector3 FlatSurface(float distance, float lane) => FlatPoint(distance) + (lane == 0 ? Vector3.Zero : Right(distance) * lane);
    internal Vector3 NativeSurface(float distance, float lane) => Ground(FlatSurface(distance, lane));
    internal bool NavigableWater(Vector3 point) => WaterCourseLevel is { } water && land is not null &&
        land.WorldBounds.Grow(-4).HasPoint(MuckwaterLayout.Flat(point)) && land.ContainsGround(point.X, point.Z) &&
        (IsWaterCourse || NativeWet(point)) && land.Ground(point.X, point.Z).Y <= water - .04f;
    internal bool NativeDry(Vector3 point) => land is null || TownRoutePlanner.Dry(land, point.X, point.Z);
    internal float WidthAt(float distance) => sprint?.WidthAt(FlatPoint(distance)) ?? HalfWidth;
    internal bool IsSpan(float distance) => sprint?.OnSpan(FlatPoint(distance)) == true;
    internal float? SpanDeck(float distance) => sprint?.DeckHeight(FlatPoint(distance), land!);
    internal Vector3 GroundedRacePoint(Vector3 point, float distance)
    {
        if (IsWaterCourse) return point with { Y = Math.Max(WaterCourseLevel!.Value, FreeGround(point).Y) };
        if (!HasGradedRoad && !HasHybridTravel) return FreeGround(point);
        float lane = (point - FlatPoint(distance)).Dot(Right(distance));
        return new Vector3(point.X, Surface(distance, lane).Y, point.Z);
    }
    public Vector3 Surface(float distance, float lane)
    {
        TrackTravelMode mode = TravelMode(distance);
        if (mode == TrackTravelMode.JetSki && WaterCourseLevel is { } water)
        {
            var wet = FlatSurface(distance, lane);
            wet.Y = IsWaterCourse || !OnWaterApproach(distance, mode) ? water : Math.Max(water, FreeGround(wet).Y);
            return wet;
        }
        if (mode == TrackTravelMode.Flight)
        {
            var ground = Ground(FlatSurface(distance, lane));
            if (WaterCourseLevel is { } flightWater && NativeWet(ground)) ground.Y = Math.Max(ground.Y, flightWater);
            return ground;
        }
        if (voidFrames is { } frames)
        {
            var (index, blend) = VoidFrameSample(distance);
            Vector3 center = frames[index].Center.Lerp(frames[index + 1].Center, blend);
            return lane == 0 ? center : center + frames[index].Right.Lerp(frames[index + 1].Right, blend).Normalized() * lane;
        }
        float width = IsSprint ? WidthAt(distance) : HalfWidth;
        if (ridgeRoad is not null && distance < DismountLine && Math.Abs(lane) <= width + .62f)
        {
            Vector3 surface = ridgeRoad.Surface(distance, lane * HalfWidth / width);
            if (SpanDeck(distance) is { } deck) return surface with { Y = deck };
            return surface with { Y = Math.Max(surface.Y, FreeGround(surface).Y) };
        }
        return Ground(FlatSurface(distance, lane));
    }
    internal void BuildGradedRoad(Node3D scene, Material dirt) => ridgeRoad?.Build(scene, this, dirt);
    internal bool OnHighTrestle(float distance, float lane) => ridgeRoad is not null &&
        Surface(distance, lane).Y - NativeSurface(distance, lane).Y > 1.3f;

    internal bool IsRoadDrop(Vector3 edge, Vector3 outside) =>
        !IsWaterCourse && (edge.Y - FreeGround(outside).Y > 1.45f || (IsCustom || IsPointToPoint) &&
        (land?.ContainsGround(outside.X, outside.Z) == false || !NativeDry(outside)));

    internal Vector3 KeepCameraOutsideScenery(Vector3 focus, Vector3 eye)
    {
        if (land is null || focus.DistanceSquaredTo(eye) > 26 * 26) return eye;
        var start = new System.Numerics.Vector3(focus.X, focus.Y, focus.Z);
        var end = new System.Numerics.Vector3(eye.X, eye.Y, eye.Z);
        var hit = land.Physics.Trace(start, end, .26f, includeTerrain: false, requireInward: true);
        if (hit is not { Fraction: > .10f and < 1 } obstacle) return eye;
        return focus.Lerp(eye, Math.Max(.10f, obstacle.Fraction - .035f));
    }

    public void BuildTownMarkers(Node3D scene)
    {
        float edge = WidthAt(Grid);
        int tiles = 12;
        float size = 2 * edge / tiles;
        for (int i = 0; i < tiles; i++)
            for (int row = 0; row < 2; row++)
                Ribbon(scene, -edge + i * size, -edge + (i + 1) * size, .09f,
                    (i + row) % 2 == 0 ? "f7e6b4" : "343c30", Grid + row * .64f, Grid + (row + 1) * .64f);

        for (float at = Grid + 15; at < Length; at += IsSprint ? 64 : 38)
            foreach (int side in new[] { -1, 1 })
            {
                if (IsSpan(at) || TravelMode(at) != TrackTravelMode.Ground) continue;
                var flag = new Node3D { Position = Surface(at, side * (WidthAt(at) + .35f)) };
                scene.AddChild(flag);
                Box(flag, new(0, 1.25f, 0), new(.09f, 2.52f, .09f), Paint("624631"));
                Box(flag, new(.2f * side, 2.16f, 0), new(.46f, .43f, .12f), Paint(side < 0 ? "e5b356" : "70c9ae"));
            }
    }
    internal MuckwaterScenery? Scenery { get; private set; }

    public void Build(Node3D scene, AcAssets assets)
    {
        var decor = new Node3D { Name = "DAT course scenery" };
        scene.AddChild(decor);
        try
        {
            var scenery = new MuckwaterScenery(assets, this);
            scenery.BuildCourse(decor, this);
            Scenery = scenery;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException or NotSupportedException)
        {
            scene.RemoveChild(decor);
            decor.QueueFree();
            GD.PushWarning("Native circuit scenery unavailable. " + error.Message);
            BuildFallbackScenery(scene);
        }
        for (float at = 0; at < Length - 1; at += 3f)
        {
            var edge = (int)(at / 3) % 2 == 0 ? "ead9a5" : "a64f38";
            Ribbon(scene, -HalfWidth - .33f, -HalfWidth + .22f, .023f, edge, at, Math.Min(at + 3, Length));
            Ribbon(scene, HalfWidth - .22f, HalfWidth + .33f, .023f, edge, at, Math.Min(at + 3, Length));
        }
        float tile = 2 * HalfWidth / 18;
        for (int i = 0; i < 18; i++)
            for (int row = 0; row < 2; row++)
                Ribbon(scene, -HalfWidth + i * tile, -HalfWidth + (i + 1) * tile, .04f,
                    (i + row) % 2 == 0 ? "f7e6b4" : "343c30", Grid + row * .64f, Grid + (row + 1) * .64f);

        BuildGate(scene, Scenery is not null);
        for (int i = 0; i < 13; i++)
        {
            float at = Grid + 32 + i * (Length - 42) / 13;
            float side = i % 3 == 1 ? 1 : -1;
            var flag = new Node3D { Position = Surface(at, side * (HalfWidth + 1.6f)) };
            scene.AddChild(flag);
            Box(flag, new(0, 1.15f, 0), new(.1f, 2.3f, .1f), Paint("564936"));
            Box(flag, new(.28f, 1.95f, 0), new(.65f, .42f, .07f), Paint(i % 2 == 0 ? "e6a744" : "53a58e"));
        }
    }

    private void BuildFallbackScenery(Node3D scene)
    {
        var moss = Paint("617d45");
        Vector2 center = MuckwaterLayout.Origin + MuckwaterLayout.Extent * .5f;
        Box(scene, new(center.X, -.8f, center.Y), new(MuckwaterLayout.Extent.X, 1, MuckwaterLayout.Extent.Y), moss);
        Box(scene, new(-27, -.26f, 12), new(30, .12f, 31), Paint("549086", .33f));
        Box(scene, new(-29, -.25f, -38), new(27, .12f, 17), Paint("56938c", .34f));
        Ribbon(scene, -HalfWidth - 1.5f, HalfWidth + 1.5f, -.12f, "929056", 0, Length);
        Ribbon(scene, -RoadHalf, RoadHalf, -.025f, "a17a55", 0, Length);
        Ribbon(scene, -HalfWidth + 1.6f, HalfWidth - 1.6f, -.015f, "af865d", 0, Length);
        var random = new Random(624);
        var bark = Paint("564936");
        var foliage = new[] { Paint("405c36"), Paint("778749"), Paint("4c7243"), Paint("8c9951") };
        var stone = Paint("777d6d");
        var rocks = new SphereMesh { RadialSegments = 6, Rings = 3 };
        var crown = new SphereMesh { RadialSegments = 7, Rings = 3 };
        var trunk = new CylinderMesh { TopRadius = .25f, BottomRadius = .5f, Height = 3, RadialSegments = 6 };
        var samples = Curve.GetBakedPoints();
        for (int i = 0; i < 490; i++)
        {
            var p = new Vector3(random.NextSingle() * 172 - 112, -.26f, random.NextSingle() * 175 - 87);
            if (samples.Any(q => q.DistanceSquaredTo(p) < (HalfWidth + 3.3f) * (HalfWidth + 3.3f))) continue;
            bool inPond = (p.X is > -42 and < -12 && p.Z is > -3 and < 27) ||
                          (p.X is > -42 and < -15 && p.Z is > -46 and < -30);
            float size = .6f + random.NextSingle() * 1.85f;
            if (inPond || i % 4 != 0)
            {
                if (inPond && i % 2 == 0) continue;
                MeshAt(scene, rocks, p, new(size * 2.3f, size * .55f, size * 1.5f), i % 3 == 0 ? stone : foliage[i % 4]);
                continue;
            }
            MeshAt(scene, trunk, p + Vector3.Up * (1.4f * size), new(size, size, size), bark);
            for (int layer = 0; layer < 3; layer++)
                MeshAt(scene, crown, p + new Vector3((layer - 1) * size * .64f, (2.8f + layer * .27f) * size, layer * .18f),
                    new(size * 3.6f, size * 2.15f, size * 3.3f), foliage[(i + layer) % 4]);
        }
        for (int i = 0; i < 22; i++)
        {
            float a = i * Mathf.Tau / 22;
            var p = new Vector3(-31 + Mathf.Cos(a) * 102, -.5f, Mathf.Sin(a) * 99);
            MeshAt(scene, rocks, p, new(28, 17 + i % 5 * 3, 28), foliage[i % 4]);
        }

    }

    private void BuildGate(Node3D scene, bool nativePillars)
    {
        var gate = new Node3D { Position = Point(Grid + .65f), Rotation = new(0, Yaw(Forward(Grid)), 0) };
        scene.AddChild(gate);
        var timber = Paint("473a2c");
        var trim = Paint("e5a743");
        var dark = Paint("263c32");
        if (!nativePillars) foreach (int side in new[] { -1, 1 })
        {
            Box(gate, new(side * (HalfWidth + .67f), 2.55f, 0), new(.47f, 5.1f, .58f), timber);
            Box(gate, new(side * (HalfWidth + .67f), 1.4f, 0), new(.72f, 2.25f, .8f), trim);
            Box(gate, new(side * (HalfWidth + .67f), .35f, 0), new(1.15f, .7f, 1.16f), dark);
        }
        float width = 2 * (HalfWidth + (nativePillars ? 3.2f : .9f));
        Box(gate, new(0, 4.88f, 0), new(width, .26f, .66f), trim);
        Box(gate, new(0, 5.48f, 0), new(width - .25f, 1.2f, .44f), dark);
        Box(gate, new(0, 6.13f, 0), new(width, .13f, .55f), trim);
        var label = new Label3D
        {
            Text = "MUCKWATER  CIRCUIT", FontSize = 96, PixelSize = .009f, OutlineSize = 2,
            Modulate = new("ffdda1"), Position = new(0, 5.48f, .25f), DoubleSided = false
        };
        gate.AddChild(label);
    }

    public void Ribbon(Node3D scene, float left, float right, float height, string color, float start, float end) =>
        Ribbon(scene, left, right, height, Paint(color), start, end);

    internal void Ribbon(Node3D scene, float left, float right, float height, Material material, float start, float end,
        Action<Vector3, Vector3, Vector3, Vector3>? addDeckQuad = null)
    {
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        int strips = Math.Max(1, (int)Math.Ceiling((end - start) / (land is null ? 3 : 1.65f)));
        for (int i = 0; i < strips; i++)
        {
            float a = Mathf.Lerp(start, end, (float)i / strips);
            float b = Mathf.Lerp(start, end, (float)(i + 1) / strips);
            var l0 = Surface(a, left) + Vector3.Up * height;
            var r0 = Surface(a, right) + Vector3.Up * height;
            var l1 = Surface(b, left) + Vector3.Up * height;
            var r1 = Surface(b, right) + Vector3.Up * height;
            addDeckQuad?.Invoke(l0, r0, l1, r1);
            foreach (var vertex in new[] { l0, l1, r0, r0, l1, r1 })
            {
                surface.SetNormal(Vector3.Up);
                surface.AddVertex(vertex);
            }
        }
        MeshAt(scene, surface.Commit(), Vector3.Zero, Vector3.One, material);
    }

    public static float Yaw(Vector3 direction) => Mathf.Atan2(-direction.X, -direction.Z);

    public static StandardMaterial3D Paint(string color, float rough = .92f) =>
        new() { AlbedoColor = new(color), Roughness = Math.Max(.84f, rough), MetallicSpecular = 0,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    public static MeshInstance3D MeshAt(Node3D parent, Mesh mesh, Vector3 at, Vector3 scale, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, Scale = scale };
        parent.AddChild(node);
        return node;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 at, Vector3 size, Material material) =>
        MeshAt(parent, new BoxMesh { Size = size }, at, Vector3.One, material);
}










