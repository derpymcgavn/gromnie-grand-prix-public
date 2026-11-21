using Godot;

public partial class GrandPrix
{
    private static void BuildVoidRoadScenery(Node3D root, BogCircuit circuit)
    {
        var paint = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true
        };
        var trackEdges = BogCircuit.MeshAt(root, BuildVoidDeckRims(circuit), Vector3.Zero, Vector3.One, paint);
        trackEdges.Name = "Deep spectral track edges";
        trackEdges.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        int scoutCount = Mathf.CeilToInt(circuit.Length / 12);
        Vector3[] route = Enumerable.Range(0, scoutCount).Select(i => circuit.Point(i * circuit.Length / scoutCount)).ToArray();
        var prisms = new List<(Transform3D Transform, Color Color)>();
        int groups = Math.Max(5, Mathf.RoundToInt(circuit.Length / 154));
        for (int i = 0; i < groups; i++)
        {
            float fraction = (i + .40f) / groups;
            float station = circuit.Length * fraction;
            float width = circuit.WidthAt(station);
            bool prominent = i % 4 == 1;
            Color tint = VoidSpectrum(fraction);
            Vector3 right = circuit.Right(station);
            right.Y = 0;
            right = right.Normalized();
            float yaw = Mathf.Atan2(right.Z, right.X);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 edge = circuit.Surface(station, side * width);
                SpawnPrism(edge + right * (side * (prominent ? 24 : 15)) + Vector3.Up * (prominent ? 4 : 1),
                    prominent ? 9.2f : 4.4f + 1.8f * MathF.Sin(i * 2.4f), prominent ? 3.5f : 2.5f);
                if (prominent)
                {
                    SpawnPrism(edge + right * (side * 40) + Vector3.Down * 4, 6.1f, 2.8f);
                    SpawnPrism(edge + right * (side * 47) + Vector3.Up * 5, 4.2f, 1.9f);
                }

                void SpawnPrism(Vector3 at, float heightScale, float widthScale)
                {
                    float horizontalClearance = width + widthScale + 6;
                    float low = at.Y - heightScale * 1.8f - 6;
                    float high = at.Y + heightScale * 3 + 6;
                    foreach (Vector3 sample in route)
                    {
                        if (sample.Y < low || sample.Y > high) continue;
                        float dx = sample.X - at.X;
                        float dz = sample.Z - at.Z;
                        if (dx * dx + dz * dz < horizontalClearance * horizontalClearance) return;
                    }
                    var basis = Basis.FromEuler(new Vector3(.17f, yaw + i * .61f, side * .22f)) *
                        Basis.FromScale(new Vector3(widthScale, heightScale, widthScale));
                    prisms.Add((new Transform3D(basis, at), tint.Lerp(new Color("cde2ff"), .22f)));
                }
            }
        }

        var collection = new MultiMesh
        {
            Mesh = BuildVoidPrism(),
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = prisms.Count
        };
        for (int i = 0; i < prisms.Count; i++)
        {
            collection.SetInstanceTransform(i, prisms[i].Transform);
            collection.SetInstanceColor(i, prisms[i].Color);
        }
        root.AddChild(new MultiMeshInstance3D
        {
            Name = "Floating prism arrays", Multimesh = collection, MaterialOverride = paint,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private static ArrayMesh BuildVoidDeckRims(BogCircuit circuit)
    {
        int sections = Mathf.CeilToInt(circuit.Length / 2);
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { -1f, 1f })
        for (int i = 0; i <= sections; i++)
        {
            float fraction = (float)i / sections;
            float at = i == sections ? 0 : fraction * circuit.Length;
            float width = circuit.WidthAt(at);
            Color light = VoidSpectrum(fraction);
            Vector3 edge = circuit.Surface(at, side * width);
            Vector3 bevel = circuit.Surface(at, side * (width - 1.5f)) - Vector3.Up * 3;
            mesh.SetColor(light * .62f);
            mesh.AddVertex(edge - Vector3.Up * .035f);
            mesh.SetColor(light * .22f);
            mesh.AddVertex(edge - Vector3.Up * .80f);
            mesh.SetColor(light * .092f);
            mesh.AddVertex(bevel);
            if (i == 0) continue;
            int end = (side < 0 ? 0 : (sections + 1) * 3) + i * 3;
            int start = end - 3;
            for (int row = 0; row < 2; row++)
            {
                mesh.AddIndex(start + row); mesh.AddIndex(end + row); mesh.AddIndex(start + row + 1);
                mesh.AddIndex(start + row + 1); mesh.AddIndex(end + row); mesh.AddIndex(end + row + 1);
            }
        }
        return mesh.Commit();
    }

    private static Color VoidSpectrum(float fraction)
    {
        float phase = fraction * 2 + .095f * MathF.Cos(Mathf.Tau * fraction) + .24f;
        return new Color(Sample(0), Sample(-.32f), Sample(.31f));
        float Sample(float offset) => .57f + .43f * MathF.Cos(Mathf.Tau * (phase + offset));
    }

    private static ArrayMesh BuildVoidPrism()
    {
        const int facets = 6;
        Vector3 crown = new(.11f, 2.9f, -.15f), root = new(-.16f, -1.6f, .07f);
        using var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < facets; i++)
        {
            Vector3 corner = Corner(i), next = Corner(i + 1);
            float light = .24f + .09f * (i % 3);
            bool brightEdge = i is 0 or 3;
            Vertex(crown, brightEdge ? .49f : .19f); Vertex(corner, brightEdge ? .69f : light); Vertex(next, light * .75f);
            Vertex(root, light * .34f); Vertex(next, light * .46f); Vertex(corner, light * .71f);
        }
        return mesh.Commit();

        static Vector3 Corner(int index)
        {
            float angle = index * Mathf.Tau / facets;
            return new Vector3(MathF.Cos(angle), .18f * MathF.Cos(2 * angle), MathF.Sin(angle));
        }

        void Vertex(Vector3 at, float light)
        {
            mesh.SetColor(new Color(light, light, light));
            mesh.AddVertex(at);
        }
    }
}
