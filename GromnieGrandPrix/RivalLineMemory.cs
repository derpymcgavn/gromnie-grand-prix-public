internal sealed class RivalLineMemory
{
    internal readonly record struct Line(float Lane, float Pace, int Samples);
    internal const float SectorLength = 32;
    private readonly Dictionary<int, Dictionary<int, Line>> courses = [];
    internal bool Dirty { get; private set; }

    internal Line? Read(int course, int sector) =>
        courses.TryGetValue(course, out var lines) && lines.TryGetValue(sector, out var line) ? line : null;

    internal void Observe(int course, int sector, float lane, float pace)
    {
        if (course <= 0 || sector is < 0 or > 511 || !float.IsFinite(lane) || !float.IsFinite(pace) ||
            Math.Abs(lane) > .82f || pace is < .65f or > 1.15f) return;
        if (!courses.TryGetValue(course, out var lines)) courses[course] = lines = [];
        if (!lines.TryGetValue(sector, out var prior)) lines[sector] = new(lane, pace, 1);
        else
        {
            // Favor a quick, repeatable line. Keep the window responsive when the driver changes their line.
            if (pace + .065f < prior.Pace) return;
            float gain = 1f / Math.Min(prior.Samples + 1, 7);
            lines[sector] = new(prior.Lane + (lane - prior.Lane) * gain,
                prior.Pace + (pace - prior.Pace) * gain, Math.Min(8, prior.Samples + 1));
        }
        Dirty = true;
    }

    internal void Restore(Dictionary<int, Dictionary<int, Line>>? saved, Func<int, bool> matchingCourse)
    {
        if (saved is null) return;
        foreach (var (course, lines) in saved)
        {
            if (course <= 0 || !matchingCourse(course) || lines is null) continue;
            foreach (var (sector, line) in lines)
            {
                if (sector is < 0 or > 511 || !float.IsFinite(line.Lane) || !float.IsFinite(line.Pace) ||
                    Math.Abs(line.Lane) > .82f || line.Pace is < .65f or > 1.15f || line.Samples is < 1 or > 8) continue;
                if (!courses.TryGetValue(course, out var local)) courses[course] = local = [];
                local[sector] = line;
            }
        }
    }

    internal Dictionary<int, Dictionary<int, Line>> SavedLines => courses;
    internal void Forget(int course) => Dirty |= courses.Remove(course);
    internal void MarkSaved() => Dirty = false;
}
