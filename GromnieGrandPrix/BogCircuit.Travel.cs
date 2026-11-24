using Godot;

internal enum TrackTravelMode { Ground, JetSki, Flight }

internal readonly record struct TrackTravelSection(TrackTravelMode Mode, float Start, float End);

public sealed partial class BogCircuit
{
    private TrackTravelSection[] travelSections = [];
    internal IReadOnlyList<TrackTravelSection> TravelSections => travelSections;
    internal bool HasHybridTravel => travelSections.Length != 0;
    internal bool HasWaterSections => WaterCourseLevel.HasValue;

    // Each control selects the mode from its smoothed spline vertex to the next one.
    private void ConfigureTravel(TrackTravelMode[]? modes)
    {
        if (modes is null || modes.All(mode => mode == TrackTravelMode.Ground)) return;
        var boundaries = new List<(TrackTravelMode Mode, float Station)>();
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i] == modes[(i + modes.Length - 1) % modes.Length]) continue;
            float station = i == 0 ? 0 : Curve.GetClosestOffset(Curve.GetPointPosition(i));
            if (boundaries.Count > 0 && station <= boundaries[^1].Station)
                throw new InvalidDataException("Two mode changes overlap. Move the nearby spline points apart.");
            boundaries.Add((modes[i], station));
        }
        if (boundaries.Count == 0)
        {
            travelSections = [new(modes[0], 0, Length)];
            return;
        }

        travelSections = new TrackTravelSection[boundaries.Count];
        for (int i = 0; i < boundaries.Count; i++)
        {
            var (mode, start) = boundaries[i];
            float end = i + 1 < boundaries.Count ? boundaries[i + 1].Station : boundaries[0].Station + Length;
            travelSections[i] = new(mode, start, end);
        }
    }

    internal TrackTravelMode TravelMode(float distance) => TravelSection(distance).Mode;

    internal TrackTravelSection TravelSection(float distance)
    {
        if (travelSections.Length == 0)
            return new(IsWaterCourse ? TrackTravelMode.JetSki : TrackTravelMode.Ground,
                float.NegativeInfinity, float.PositiveInfinity);
        float lap = MathF.Floor(distance / Length) * Length;
        float at = distance - lap;
        int low = 0, high = travelSections.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (travelSections[mid].Start <= at) low = mid + 1;
            else high = mid;
        }
        if (low == 0) lap -= Length;
        TrackTravelSection section = travelSections[(low + travelSections.Length - 1) % travelSections.Length];
        return new(section.Mode, section.Start + lap, section.End + lap);
    }

    internal bool NativeWet(Vector3 point) => land is not null &&
        land.ContainsGround(point.X, point.Z) && !TownRoutePlanner.Dry(land, point.X, point.Z);

    private float InferHybridWaterLevel()
    {
        // DAT water uses flat elevations. Pick the level matching most of the marked
        // channel rather than a sloped bank vertex at one end of the route.
        var heights = new Dictionary<int, int>();
        foreach (var section in travelSections)
        {
            if (section.Mode != TrackTravelMode.JetSki) continue;
            int samples = Math.Max(3, Mathf.CeilToInt((section.End - section.Start) / 10));
            for (int i = 0; i < samples; i++)
            {
                float at = Mathf.Lerp(section.Start, section.End, (i + .5f) / samples);
                Vector3 point = FlatPoint(at);
                if (!NativeWet(point)) continue;
                int height = Mathf.RoundToInt(FreeGround(point).Y * 4);
                heights[height] = heights.GetValueOrDefault(height) + 1;
            }
        }
        if (heights.Count == 0)
            throw new InvalidDataException("Place the Jet Ski section over native water.");
        return heights.OrderByDescending(row => row.Value).ThenByDescending(row => row.Key).First().Key * .25f + .19f;
    }

    internal bool OnWaterApproach(float distance, TrackTravelMode mode)
    {
        if (!HasHybridTravel || !HasWaterSections || travelSections.Length < 2) return false;
        TrackTravelSection section = TravelSection(distance);
        if (section.Mode != mode) return false;
        if (mode == TrackTravelMode.JetSki)
        {
            float margin = Math.Min(24, (section.End - section.Start) * .20f);
            return distance - section.Start < margin || section.End - distance < margin;
        }
        return distance - section.Start < 17 && TravelMode(section.Start - .05f) == TrackTravelMode.JetSki ||
            section.End - distance < 17 && TravelMode(section.End + .05f) == TrackTravelMode.JetSki;
    }
}
