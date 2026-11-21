using Godot;

internal sealed class VoidRoadSurface
{
    private const float CellSize = 36;
    internal const float MeshStep = 2;
    private readonly Section[] sections;
    private readonly Dictionary<Vector2I, List<int>> cells = [];

    private readonly record struct Section(Triangle Near, Triangle Far, Vector3 Min, Vector3 Max);

    private readonly struct Triangle
    {
        private readonly Vector3 origin;
        private readonly Vector2 uGradient, vGradient, heightGradient;
        private readonly float maxHeight;

        internal Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            origin = a;
            Vector3 edge = b - a, side = c - a;
            float inverseArea = 1 / (edge.X * side.Z - edge.Z * side.X);
            uGradient = new Vector2(side.Z, -side.X) * inverseArea;
            vGradient = new Vector2(-edge.Z, edge.X) * inverseArea;
            heightGradient = uGradient * edge.Y + vGradient * side.Y;
            maxHeight = Math.Max(a.Y, Math.Max(b.Y, c.Y));
        }

        internal bool TryHeightAt(Vector3 point, out float height)
        {
            Vector2 offset = new(point.X - origin.X, point.Z - origin.Z);
            float u = uGradient.Dot(offset), v = vGradient.Dot(offset);
            height = origin.Y + heightGradient.Dot(offset);
            return u >= -.0001f && v >= -.0001f && u + v <= 1.0001f;
        }

        internal float HeightCeiling(float minX, float maxX, float minZ, float maxZ)
        {
            // The plane reaches its maximum at a corner of the part's footprint.
            float x = heightGradient.X > 0 ? maxX : minX;
            float z = heightGradient.Y > 0 ? maxZ : minZ;
            return Math.Min(maxHeight, origin.Y + heightGradient.Dot(new Vector2(x - origin.X, z - origin.Z)));
        }
    }

    internal VoidRoadSurface(BogCircuit track)
    {
        float length = track.Length;
        sections = new Section[Mathf.CeilToInt(length / MeshStep)];
        float width = track.HalfWidth;
        Vector3 left = track.Surface(0, -width), right = track.Surface(0, width);
        for (int i = 0; i < sections.Length; i++)
        {
            float station = i + 1 == sections.Length ? 0 : (i + 1) / (float)sections.Length * length;
            Vector3 nextLeft = track.Surface(station, -width), nextRight = track.Surface(station, width);
            var bounds = new Aabb(left, Vector3.Zero).Expand(right).Expand(nextLeft).Expand(nextRight);
            sections[i] = new Section(new Triangle(left, nextLeft, right), new Triangle(right, nextLeft, nextRight),
                bounds.Position, bounds.End);
            Vector2I min = Cell(bounds.Position), max = Cell(bounds.End);
            for (int x = min.X; x <= max.X; x++)
            for (int z = min.Y; z <= max.Y; z++)
            {
                var cell = new Vector2I(x, z);
                if (!cells.TryGetValue(cell, out var entries)) cells[cell] = entries = [];
                entries.Add(i);
            }
            left = nextLeft;
            right = nextRight;
        }
    }

    internal float HeightAt(Vector3 point)
    {
        if (!cells.TryGetValue(Cell(point), out var entries)) return float.NegativeInfinity;
        float closest = float.NegativeInfinity, gap = float.PositiveInfinity;
        foreach (int index in entries)
        {
            ref readonly var section = ref sections[index];
            if (point.X < section.Min.X || point.X > section.Max.X || point.Z < section.Min.Z || point.Z > section.Max.Z) continue;
            if (!section.Near.TryHeightAt(point, out float top) && !section.Far.TryHeightAt(point, out top)) continue;
            float distance = Math.Abs(top - point.Y);
            if (distance >= gap) continue;
            closest = top;
            gap = distance;
        }
        return closest;
    }

    internal float HighestNear(Aabb bounds)
    {
        float highest = float.NegativeInfinity;
        Vector3 low = bounds.Position, high = bounds.End;
        Vector2I min = Cell(low), max = Cell(high);
        for (int x = min.X; x <= max.X; x++)
        for (int z = min.Y; z <= max.Y; z++)
        {
            if (!cells.TryGetValue(new Vector2I(x, z), out var entries)) continue;
            foreach (int index in entries)
            {
                ref readonly var section = ref sections[index];
                if (section.Min.X > high.X || section.Max.X < low.X || section.Min.Z > high.Z ||
                    section.Max.Z < low.Z || section.Min.Y > high.Y + 8 || section.Max.Y <= highest) continue;
                float minX = Math.Max(low.X, section.Min.X), maxX = Math.Min(high.X, section.Max.X);
                float minZ = Math.Max(low.Z, section.Min.Z), maxZ = Math.Min(high.Z, section.Max.Z);
                highest = Math.Max(highest, Math.Max(section.Near.HeightCeiling(minX, maxX, minZ, maxZ),
                    section.Far.HeightCeiling(minX, maxX, minZ, maxZ)));
            }
        }
        return highest;
    }

    private static Vector2I Cell(Vector3 point) => new(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Z / CellSize));
}
