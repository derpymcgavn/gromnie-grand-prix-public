using Godot;

public partial class GrandPrix
{
    // The outer crystal of the Shard of the Herald setup.
    private const uint KaleidoscopeCrystalModel = 0x01001a1b;
    private const int KaleidoscopePetalCount = 8;
    private sealed record KaleidoscopeChamber(Node3D Rosette, Node3D Lattice, float Speed);
    private readonly List<KaleidoscopeChamber> kaleidoscopeChambers = [];
    private Node3D? kaleidoscopePrismRoot;

    private void BuildKaleidoscopePrisms(Node3D root, BogCircuit circuit)
    {
        ClearKaleidoscopePrisms();
        kaleidoscopePrismRoot = new Node3D { Name = "Kaleidoscope prisms" };
        root.AddChild(kaleidoscopePrismRoot);

        var quartz = new List<Transform3D>(KaleidoscopePetalCount * 3);
        var cores = new List<Transform3D>(KaleidoscopePetalCount);
        var sparks = new List<Transform3D>(KaleidoscopePetalCount * 2);
        var heraldShards = new List<Transform3D>(KaleidoscopePetalCount);
        for (int petal = 0; petal < KaleidoscopePetalCount; petal++)
        {
            float angle = petal * Mathf.Tau / KaleidoscopePetalCount;
            quartz.Add(Prism(angle, 35.1f, 0, new(6.7f, 12.1f, 7), .29f));
            quartz.Add(Prism(angle - .15f, 37.2f, -1.9f, new(4.5f, 8.6f, 5), -.41f, -.18f));
            quartz.Add(Prism(angle + .15f, 37.2f, 1.2f, new(4.5f, 8.6f, 5), .41f, .18f));
            // Thin luminous facets rise through the front of each main crystal.
            cores.Add(Prism(angle + .019f, 34.8f, 2.75f, new(1.45f, 8.1f, 1.78f), .29f));
            sparks.Add(Prism(angle - .25f, 37.8f, -4.5f, new(1.65f, 5.2f, 2.1f), .25f));
            sparks.Add(Prism(angle + .27f, 38.6f, -4.9f, new(1.22f, 3.7f, 1.6f), -.35f));
            heraldShards.Add(Prism(angle + Mathf.Pi / KaleidoscopePetalCount, 37.3f, -4.3f, new(4.5f, 8.3f, 3.9f), .36f));
        }
        var quartzMesh = BakeKaleidoscopePrisms(quartz);
        var coreMesh = BakeKaleidoscopePrisms(cores);
        var sparkMesh = BakeKaleidoscopePrisms(sparks);
        Mesh heraldShell = assets.Mesh(KaleidoscopeCrystalModel);
        Aabb heraldBounds = heraldShell.GetAabb();
        for (int i = 0; i < heraldShards.Count; i++)
        {
            Transform3D pose = heraldShards[i].ScaledLocal(Vector3.One / heraldBounds.Size);
            pose.Origin -= pose.Basis * heraldBounds.GetCenter();
            heraldShards[i] = pose;
        }
        var nativePaint = heraldShell.SurfaceGetMaterial(0) as StandardMaterial3D;

        var colors = new (Color Quartz, Color Core, Color Accent)[]
        {
            (new("478eff"), new("7bd8ff"), new("94b4ff")),
            (new("25cbba"), new("73fce3"), new("a9ffe7")),
            (new("8853eb"), new("daa2ff"), new("be97ff")),
            (new("d95a92"), new("ff9bc5"), new("ffafcd")),
            (new("da963d"), new("ffe2a0"), new("ffc581"))
        };
        var treatments = colors.Select(color => (
            Quartz: CrystalFinish(color.Quartz, .31f, .12f, .08f),
            Core: CrystalFinish(color.Core, .14f, .17f, 1.85f),
            Spark: CrystalFinish(color.Accent, .21f, .17f, 1.18f),
            Herald: CrystalFinish(color.Accent, .48f, .14f, .17f, nativePaint?.AlbedoTexture)
        )).ToArray();

        int count = Mathf.Clamp(Mathf.RoundToInt(circuit.Length / 155), 10, 32);
        float spacing = circuit.Length / count, firstStation = Math.Min(164, spacing * .94f);
        for (int i = 0; i < count; i++)
        {
            float station = firstStation + i * spacing;
            var chamber = new Node3D
            {
                Name = $"Prism chamber {i + 1}",
                Position = circuit.Point(station) + Vector3.Up * 1.8f,
                Basis = Basis.LookingAt(circuit.Forward(station))
            };
            kaleidoscopePrismRoot.AddChild(chamber);
            var rosette = new Node3D { Name = "Rosette", Rotation = Vector3.Back * (i * .39f + .23f) };
            var counterweight = new Node3D { Name = "Counter-rotating lattice", Rotation = Vector3.Back * (-i * .25f) };
            chamber.AddChild(rosette);
            chamber.AddChild(counterweight);
            int palette = i * colors.Length / count;
            var treatment = treatments[palette];
            Place(rosette, "Cut quartz petals", quartzMesh, treatment.Quartz);
            Place(rosette, "Luminous inclusions", coreMesh, treatment.Core);
            Place(counterweight, "Floating light splinters", sparkMesh, treatment.Spark);
            AttachHeraldShards(counterweight, heraldShell, treatment.Herald, heraldShards);
            AddLight(chamber, "Crystal fill", new(16, 23, 2.3f), colors[palette].Core, 2.25f);
            AddLight(chamber, "Opposing crystal fill", new(-16, 21, -3), colors[palette].Accent, 1.50f);
            kaleidoscopeChambers.Add(new KaleidoscopeChamber(rosette, counterweight, .165f + .021f * (i % 4)));
        }

        static Transform3D Prism(float angle, float radius, float axial, Vector3 size, float twist, float cant = 0)
        {
            var orientation = new Basis(Vector3.Back, angle) * new Basis(Vector3.Up, twist) * new Basis(Vector3.Back, cant);
            Vector3 pivot = new(-Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, axial);
            return new Transform3D(orientation * Basis.FromScale(size), pivot);
        }

        static void AddLight(Node3D chamber, string label, Vector3 at, Color color, float energy) =>
            chamber.AddChild(new OmniLight3D
            {
                Name = label, Position = at, LightColor = color, LightEnergy = energy, OmniRange = 53, OmniAttenuation = 1.12f
            });

        static void Place(Node3D assembly, string label, Mesh mesh, Material finish) =>
            assembly.AddChild(new MeshInstance3D
            {
                Name = label, Mesh = mesh, MaterialOverride = finish, VisibilityRangeEnd = 435, VisibilityRangeEndMargin = 35
            });
    }

    private static StandardMaterial3D CrystalFinish(Color tint, float metallic, float roughness, float energy,
        Texture2D? nativeTexture = null) => new()
    {
        AlbedoColor = tint,
        AlbedoTexture = nativeTexture,
        VertexColorUseAsAlbedo = nativeTexture is null,
        Metallic = metallic,
        MetallicSpecular = .65f,
        Roughness = roughness,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        EmissionEnabled = true,
        Emission = tint,
        EmissionEnergyMultiplier = energy
    };

    private static ArrayMesh BakeKaleidoscopePrisms(IReadOnlyList<Transform3D> prisms)
    {
        // Three irregular bands give each piece stepped shoulders and a long cleaved point.
        float[] heights = [-.23f, .16f, .36f];
        float[] radii = [.48f, .43f, .31f];
        float[] facets = [.78f, .95f, .56f, .89f, .65f, 1];
        var points = new Vector3[3, 6];
        for (int row = 0; row < heights.Length; row++)
        for (int edge = 0; edge < facets.Length; edge++)
        {
            float angle = edge * Mathf.Tau / facets.Length + .21f;
            float radius = radii[row] * (edge % 2 == 0 ? 1 : .87f);
            points[row, edge] = new Vector3(Mathf.Cos(angle) * radius + row * .028f, heights[row],
                Mathf.Sin(angle) * radius - row * .02f);
        }

        using var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var pose in prisms)
        {
            Basis normalMatrix = pose.Basis.Inverse().Transposed();
            for (int edge = 0; edge < facets.Length; edge++)
            {
                int adjacent = (edge + 1) % facets.Length;
                Vector3 outward = (points[0, edge] + points[0, adjacent]) with { Y = 0 };
                float shade = facets[edge];
                Triangle(new(-.08f, -.52f, .04f), points[0, edge], points[0, adjacent], outward, shade * .83f);
                for (int row = 0; row < heights.Length - 1; row++)
                {
                    Vector3 a = points[row, edge], b = points[row, adjacent];
                    Vector3 c = points[row + 1, edge], d = points[row + 1, adjacent];
                    Triangle(a, b, d, outward, shade);
                    Triangle(a, d, c, outward, shade * .91f);
                }
                Triangle(points[2, edge], points[2, adjacent], new(.12f, .50f, -.11f), outward + Vector3.Up, shade * .82f);
            }

            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, float shade)
            {
                Vector3 normal = (c - a).Cross(b - a);
                if (normal.Dot(outward) < 0)
                {
                    (b, c) = (c, b);
                    normal = -normal;
                }
                surface.SetNormal((normalMatrix * normal).Normalized());
                surface.SetColor(new Color(shade, shade, shade));
                surface.AddVertex(pose * a);
                surface.AddVertex(pose * b);
                surface.AddVertex(pose * c);
            }
        }
        return surface.Commit();
    }

    private static void AttachHeraldShards(Node3D assembly, Mesh mesh, Material finish, IReadOnlyList<Transform3D> poses)
    {
        var batch = new MultiMesh
        {
            Mesh = mesh, TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = poses.Count,
            CustomAabb = new Aabb(new Vector3(-43, -43, -7), new Vector3(86, 86, 14))
        };
        for (int i = 0; i < poses.Count; i++) batch.SetInstanceTransform(i, poses[i]);
        assembly.AddChild(new MultiMeshInstance3D
        {
            Name = "Native Herald shard fragments", Multimesh = batch, MaterialOverride = finish,
            VisibilityRangeEnd = 390, VisibilityRangeEndMargin = 34
        });
    }

    private void TickKaleidoscopePrisms(float dt)
    {
        foreach (var chamber in kaleidoscopeChambers)
        {
            chamber.Rosette.RotateZ(chamber.Speed * dt);
            chamber.Lattice.RotateZ(-chamber.Speed * .64f * dt);
        }
    }

    private void ClearKaleidoscopePrisms()
    {
        kaleidoscopeChambers.Clear();
        if (IsInstanceValid(kaleidoscopePrismRoot) && !kaleidoscopePrismRoot!.IsQueuedForDeletion())
            kaleidoscopePrismRoot.QueueFree();
        kaleidoscopePrismRoot = null;
    }
}
