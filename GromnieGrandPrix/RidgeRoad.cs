using Godot;

// Shared road surface for the native courses and the track editor.
internal sealed class RidgeRoad
{
    private readonly int rings;
    private readonly float step;
    private readonly bool open;
    internal const float CustomShoulder = 1.15f;
    private const float RoadCellSize = 4;
    private readonly List<DeckQuad> roadQuads = [];
    private readonly Dictionary<Vector2I, RoadCell> roadCells = [];
    private readonly Vector3[] roadVertices = [];
    private readonly float[] laneOffsets = [];

    private sealed class RoadCell
    {
        public readonly List<int> Quads = [];
        public float Top = float.NegativeInfinity;
    }

    private readonly record struct DeckQuad(Vector3 Left, Vector3 Right, Vector3 NextLeft, Vector3 NextRight)
    {
        public float? HeightAt(Vector3 point) =>
            TriangleHeight(point, Left, NextLeft, Right) ?? TriangleHeight(point, Right, NextLeft, NextRight);

        private static float? TriangleHeight(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 edge = b - a, across = c - a, delta = point - a;
            float area = edge.X * across.Z - edge.Z * across.X;
            if (Math.Abs(area) < .0001f) return null;
            float u = (delta.X * across.Z - delta.Z * across.X) / area;
            float v = (edge.X * delta.Z - edge.Z * delta.X) / area;
            return u >= -.001f && v >= -.001f && u + v <= 1.001f
                ? a.Y + edge.Y * u + across.Y * v : null;
        }
    }

    public RidgeRoad(BogCircuit track)
    {
        int segments = Mathf.CeilToInt(track.Length / .95f);
        open = track.IsSprint;
        step = track.Length / segments;
        rings = segments + (open ? 1 : 0);
        float inner = track.HalfWidth - CustomShoulder;
        int divisions = Mathf.CeilToInt(inner / 2.65f);
        laneOffsets = [-track.HalfWidth, .. Enumerable.Range(-divisions, 2 * divisions + 1)
            .Select(lane => lane * inner / divisions), track.HalfWidth];
        roadVertices = GradeRoad(track);
    }

    private Vector3[] GradeRoad(BogCircuit track)
    {
        int columns = laneOffsets.Length;
        var road = new Vector3[rings * columns];
        var bridge = new float?[rings];
        for (int i = 0; i < rings; i++)
        {
            float at = i * step;
            Vector3 center = track.NativeSurface(at, 0), right = track.Right(at), forward = track.Forward(at);
            float scale = track.WidthAt(at) / track.HalfWidth;
            bridge[i] = track.SpanDeck(at);
            for (int c = 0; c < columns; c++)
            {
                Vector3 point = track.FreeGround(center + right * (laneOffsets[c] * scale));
                if (bridge[i] is { } deck)
                {
                    point.Y = deck;
                }
                else
                {
                    // Give adjoining triangles room above the water even when the shoreline cuts between vertices.
                    float shoulder = (laneOffsets[Math.Min(c + 1, columns - 1)] - laneOffsets[Math.Max(0, c - 1)]) * scale * .31f;
                    float wet = track.NativeDry(point) ? float.NegativeInfinity : point.Y;
                    if (float.IsNegativeInfinity(wet))
                    {
                        wet = WetFloor(point + right * shoulder + forward * .58f, wet);
                        wet = WetFloor(point - right * shoulder + forward * .58f, wet);
                        wet = WetFloor(point + right * shoulder - forward * .58f, wet);
                        wet = WetFloor(point - right * shoulder - forward * .58f, wet);
                    }
                    point.Y = Math.Max(point.Y + .065f, wet + 2.70f);
                }
                road[i * columns + c] = point;
            }
        }

        var crestLift = new float[road.Length];
        for (int i = 0; i < (open ? rings - 1 : rings); i++)
        {
            int next = (i + 1) % rings;
            if (bridge[i] is not null || bridge[next] is not null) continue;
            for (int c = 0; c < columns; c++)
            {
                int a = i * columns + c, b = next * columns + c;
                float lift = 0;
                for (int part = 1; part <= 3; part++)
                {
                    Vector3 between = road[a].Lerp(road[b], part * .25f);
                    lift = Math.Max(lift, track.FreeGround(between).Y + .04f - between.Y);
                }
                crestLift[a] = Math.Max(crestLift[a], lift);
                crestLift[b] = Math.Max(crestLift[b], lift);
            }
        }
        for (int i = 0; i < road.Length; i++) road[i].Y += crestLift[i];

        const float acrossGrade = .57f;
        for (int i = 0; i < road.Length; i += columns)
        {
            float scale = track.WidthAt(i / columns * step) / track.HalfWidth;
            for (int c = 1; c < columns; c++)
                road[i + c].Y = Math.Max(road[i + c].Y,
                    road[i + c - 1].Y - acrossGrade * scale * (laneOffsets[c] - laneOffsets[c - 1]));
            for (int c = columns - 2; c >= 0; c--)
                road[i + c].Y = Math.Max(road[i + c].Y,
                    road[i + c + 1].Y - acrossGrade * scale * (laneOffsets[c + 1] - laneOffsets[c]));
        }
        var grade = new float[rings];
        float rise = step * (open ? .43f : track.IsCustom ? .61f : .48f);
        var eased = new float[rings];
        for (int c = 0; c < columns; c++)
        {
            for (int i = 0; i < rings; i++) grade[i] = road[i * columns + c].Y;
            for (int i = 0; i < (open ? rings - 1 : rings * 2); i++)
            {
                int at = i % rings, next = (at + 1) % rings;
                grade[next] = Math.Max(grade[next], grade[at] - rise);
            }
            for (int i = (open ? rings : rings * 2) - 1; i >= (open ? 1 : 0); i--)
            {
                int at = i % rings, previous = (at + rings - 1) % rings;
                grade[previous] = Math.Max(grade[previous], grade[at] - rise);
            }
            for (int i = 0; i < rings; i++)
                eased[i] = Math.Max(grade[i], Math.Max(grade[Before(i)], grade[After(i)]));
            for (int i = 0; i < rings; i++)
            {
                float average = (eased[Before(i)] + 2 * eased[i] + eased[After(i)]) * .25f;
                road[i * columns + c].Y = bridge[i] ?? average;
            }
        }
        return road;

        int Before(int ring) => open ? Math.Max(0, ring - 1) : (ring + rings - 1) % rings;
        int After(int ring) => open ? Math.Min(rings - 1, ring + 1) : (ring + 1) % rings;
        float WetFloor(Vector3 at, float floor) =>
            track.NativeDry(at) ? floor : Math.Max(floor, track.FreeGround(at).Y);
    }
    private Vector3 RoadVertex(int ring, int column) => roadVertices[ring * laneOffsets.Length + column];

    internal Vector3 Surface(float distance, float lane)
    {
        float sample = open ? Mathf.Clamp(distance / step, 0, rings - 1) : Mathf.PosMod(distance / step, rings);
        int at = Math.Min((int)sample, rings - 1);
        int next = open ? Math.Min(at + 1, rings - 1) : (at + 1) % rings;
        int found = Array.BinarySearch(laneOffsets, lane);
        int left = Math.Clamp((found >= 0 ? found : ~found) - 1, 0, laneOffsets.Length - 2);
        float lateral = (lane - laneOffsets[left]) / (laneOffsets[left + 1] - laneOffsets[left]);
        var top = new DeckQuad(RoadVertex(at, left), RoadVertex(at, left + 1),
            RoadVertex(next, left), RoadVertex(next, left + 1));
        var point = top.Left.Lerp(top.Right, lateral).Lerp(top.NextLeft.Lerp(top.NextRight, lateral), sample - at);
        return point with { Y = top.HeightAt(point) ?? point.Y };
    }

    private Vector2I CellAt(Vector3 point) =>
        new(Mathf.FloorToInt(point.X / RoadCellSize), Mathf.FloorToInt(point.Z / RoadCellSize));

    private void AddRoadQuad(Vector3 left, Vector3 right, Vector3 nextLeft, Vector3 nextRight)
    {
        var first = CellAt(left.Min(right).Min(nextLeft).Min(nextRight));
        var last = CellAt(left.Max(right).Max(nextLeft).Max(nextRight));
        float highest = Math.Max(Math.Max(left.Y, right.Y), Math.Max(nextLeft.Y, nextRight.Y));
        int index = roadQuads.Count;
        roadQuads.Add(new DeckQuad(left, right, nextLeft, nextRight));
        for (int z = first.Y; z <= last.Y; z++)
        for (int x = first.X; x <= last.X; x++)
        {
            var cell = new Vector2I(x, z);
            if (!roadCells.TryGetValue(cell, out var road)) roadCells[cell] = road = new RoadCell();
            road.Quads.Add(index);
            road.Top = Math.Max(road.Top, highest);
        }
    }

    internal float RoadHeight(Vector3 point, float floor)
    {
        if (roadCells.TryGetValue(CellAt(point), out var road) && road.Top > floor)
            foreach (int index in road.Quads)
                if (roadQuads[index].HeightAt(point) is { } top) floor = Math.Max(floor, top);
        return floor;
    }

    internal float HighestRoadIn(Aabb bounds)
    {
        Vector2I min = CellAt(bounds.Position), max = CellAt(bounds.End);
        float highest = float.NegativeInfinity;
        for (int z = min.Y; z <= max.Y; z++)
        for (int x = min.X; x <= max.X; x++)
            if (roadCells.TryGetValue(new Vector2I(x, z), out var road))
                highest = Math.Max(highest, road.Top);
        return highest;
    }

    public void Build(Node3D scene, BogCircuit track, Material dirtAndStone)
    {
        var root = new Node3D { Name = "Terrain-following road" };
        scene.AddChild(root);
        roadQuads.Clear();
        roadCells.Clear();
        int last = laneOffsets.Length - 1, count = rings;
        int segments = open ? count - 1 : count;
        var exposed = new bool[segments];
        for (int i = 0; i < segments; i++)
            exposed[i] = track.SpanDeck((i + .5f) * step) is null;
        Rect2 terrainBounds = track.NativeBounds.Grow(-.06f);
        var transitions = new (Vector3 Inner, Vector3 Outer)[count * 2];
        for (int i = 0; i < count; i++)
        {
            float distance = i * step;
            Vector3 right = track.Right(distance);
            float turn = track.Curvature(distance);
            transitions[i * 2] = Verge(RoadVertex(i, 0), -right, -turn);
            transitions[i * 2 + 1] = Verge(RoadVertex(i, last), right, turn);
        }

        BuildMesh("Continuous dirt road", last - 1, (ring, column) => RoadVertex(ring, column + 1),
            _ => new Color(.14f, 1, 0));
        BuildMesh("Left packed shoulder", 2, RoadVertex,
            col => new Color(col == 0 ? .22f : .14f, 1, 0));
        BuildMesh("Right packed shoulder", 2, (ring, col) => RoadVertex(ring, last - 1 + col),
            col => new Color(col == 0 ? .14f : .22f, 1, 0));
        BuildMesh("Left feathered verge", 3, (ring, col) => col switch
        {
            0 => transitions[ring * 2].Outer,
            1 => transitions[ring * 2].Inner,
            _ => RoadVertex(ring, 0)
        }, col => VergeColor(2 - col));
        BuildMesh("Right feathered verge", 3, (ring, col) => col switch
        {
            0 => RoadVertex(ring, last),
            1 => transitions[ring * 2 + 1].Inner,
            _ => transitions[ring * 2 + 1].Outer
        }, VergeColor);

        static Color VergeColor(int outwards) => outwards switch
        {
            0 => new Color(.22f, 1, 0),
            1 => new Color(.12f, .48f, 0),
            _ => new Color(0, 0, 0)
        };

        (Vector3 Inner, Vector3 Outer) Verge(Vector3 shoulder, Vector3 outwards, float turn)
        {
            float fill = shoulder.Y - track.FreeGround(shoulder).Y;
            float spread = Mathf.Clamp(3.6f + fill * 1.15f, 3.6f, 8f);
            if (turn > .0001f)
                spread = Math.Min(spread, Math.Max(.42f, 1 / turn - track.HalfWidth - .30f));
            Vector3 toe = shoulder + outwards * spread;
            toe.X = Mathf.Clamp(toe.X, terrainBounds.Position.X, terrainBounds.End.X);
            toe.Z = Mathf.Clamp(toe.Z, terrainBounds.Position.Y, terrainBounds.End.Y);
            Vector3 inner = track.FreeGround(shoulder.Lerp(toe, .53f));
            inner.Y += Math.Max(.035f, fill * .34f);
            return (inner, track.FreeGround(toe) - Vector3.Up * .32f);
        }

        void BuildMesh(string name, int columns, Func<int, int, Vector3> vertex, Func<int, Color> paint)
        {
            using var mesh = new SurfaceTool();
            mesh.Begin(Mesh.PrimitiveType.Triangles);
            for (int i = 0; i < count; i++)
            {
                int next = open ? Math.Min(i + 1, count - 1) : (i + 1) % count;
                int previous = open ? Math.Max(0, i - 1) : (i + count - 1) % count;
                for (int c = 0; c < columns; c++)
                {
                    var along = vertex(next, c) - vertex(previous, c);
                    var across = vertex(i, Math.Min(c + 1, columns - 1)) - vertex(i, Math.Max(c - 1, 0));
                    mesh.SetNormal(across.Cross(along).Normalized());
                    mesh.SetColor(paint(c));
                    mesh.AddVertex(vertex(i, c));
                    if (c == columns - 1 || i >= segments || !exposed[i]) continue;
                    int a = i * columns + c, b = next * columns + c;
                    mesh.AddIndex(a); mesh.AddIndex(b); mesh.AddIndex(a + 1);
                    mesh.AddIndex(a + 1); mesh.AddIndex(b); mesh.AddIndex(b + 1);
                    AddRoadQuad(vertex(i, c), vertex(i, c + 1), vertex(next, c), vertex(next, c + 1));
                }
            }
            var instance = BogCircuit.MeshAt(root, mesh.Commit(), Vector3.Zero, Vector3.One, dirtAndStone);
            instance.Name = name;
            instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
    }
}
