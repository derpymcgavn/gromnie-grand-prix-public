using Godot;

public partial class GrandPrix
{
    private const uint VoidCrystalLayer = 1u << 16;
    private const uint VoidAmethystLayer = 1u << 11;
    private const uint VoidReflectionSceneryLayer = 1u << 12;

    private static ShaderMaterial CreateVoidCrystalGlass(StandardMaterial3D? native = null, bool useVertexTint = false)
    {
        var glass = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidCrystalGlass.gdshader") };
        glass.SetShaderParameter("use_vertex_tint", useVertexTint);
        glass.SetShaderParameter("aerbax_light", VoidAerbaxPosition + Vector3.Up * 70);
        glass.SetShaderParameter("asheron_light", VoidAsheronPosition + Vector3.Up * 64);
        if (native is not null)
        {
            glass.SetShaderParameter("crystal_tint", native.AlbedoColor.Lerp(new Color("b5d9ff"), .46f));
            if (native.AlbedoTexture is { } texture)
            {
                glass.SetShaderParameter("native_texture", texture);
                glass.SetShaderParameter("use_native_texture", true);
            }
        }
        return glass;
    }

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
        trackEdges.Layers |= VoidReflectionSceneryLayer;

        int scoutCount = Mathf.CeilToInt(circuit.Length / 12);
        Vector3[] route = Enumerable.Range(0, scoutCount).Select(i => circuit.Point(i * circuit.Length / scoutCount)).ToArray();
        var prismMesh = BuildVoidAmethystCluster();
        Aabb clusterBounds = prismMesh.GetAabb();
        var prisms = new List<(Transform3D Transform, Color Color)>();
        int groups = Math.Max(5, Mathf.RoundToInt(circuit.Length / 154));
        Color deepAmethyst = new("6934a2"), paleAmethyst = new("bd92e4");
        for (int i = 0; i < groups; i++)
        {
            float fraction = (i + .40f) / groups;
            float station = circuit.Length * fraction;
            float width = circuit.WidthAt(station);
            bool prominent = i % 4 == 1;
            float hue = .5f + .5f * MathF.Sin(fraction * Mathf.Tau * 2f);
            Color tint = deepAmethyst.Lerp(paleAmethyst, hue);
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
                    var basis = Basis.FromEuler(new Vector3(.17f, yaw + i * .61f, side * .22f)) *
                        Basis.FromScale(new Vector3(widthScale, heightScale, widthScale));
                    var pose = new Transform3D(basis, at);
                    float radius = 0, low = at.Y, high = at.Y;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 point = pose * clusterBounds.GetEndpoint(corner);
                        radius = MathF.Max(radius, new Vector2(point.X - at.X, point.Z - at.Z).Length());
                        low = MathF.Min(low, point.Y);
                        high = MathF.Max(high, point.Y);
                    }
                    float horizontalClearance = width + radius + 4;
                    foreach (Vector3 sample in route)
                    {
                        if (sample.Y < low - 6 || sample.Y > high + 6) continue;
                        float dx = sample.X - at.X;
                        float dz = sample.Z - at.Z;
                        if (dx * dx + dz * dz < horizontalClearance * horizontalClearance) return;
                    }
                    prisms.Add((pose, tint));
                }
            }
        }

        var amethyst = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/VoidAmethyst.gdshader") };
        // Keep transparent sorting local instead of giving the entire orbit
        // one sort position. Each sector still draws as one instanced mesh.
        foreach (var sector in prisms.GroupBy(prism =>
            (Mathf.FloorToInt(prism.Transform.Origin.X / 285), Mathf.FloorToInt(prism.Transform.Origin.Z / 285))))
        {
            var batch = sector.ToArray();
            Vector3 center = new((sector.Key.Item1 + .5f) * 285, batch.Average(prism => prism.Transform.Origin.Y),
                (sector.Key.Item2 + .5f) * 285);
            var collection = new MultiMesh
            {
                Mesh = prismMesh, TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true, InstanceCount = batch.Length
            };
            for (int i = 0; i < batch.Length; i++)
            {
                var transform = batch[i].Transform;
                transform.Origin -= center;
                collection.SetInstanceTransform(i, transform);
                collection.SetInstanceColor(i, batch[i].Color);
            }
            collection.CustomAabb = collection.GetAabb().Grow(5);
            root.AddChild(new MultiMeshInstance3D
            {
                Name = $"Floating prisms {sector.Key.Item1}, {sector.Key.Item2}",
                Position = center, Multimesh = collection, MaterialOverride = amethyst,
                Layers = VoidAmethystLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        const uint battleScenery = VoidCasterLightLayer | VoidAerbaxLightLayer | VoidAsheronLightLayer;
        // Capture the course and battle scenery once. Reflective actors and racers stay outside the cubemaps.
        root.AddChild(new ReflectionProbe
        {
            Name = "Void crystal reflections", Position = new Vector3(0, 145, 0),
            Size = new Vector3(1400, 590, 1400), MaxDistance = 1650, BlendDistance = 44,
            CullMask = battleScenery | VoidReflectionSceneryLayer,
            ReflectionMask = VoidCrystalLayer | VoidAmethystLayer, Intensity = 1.35f,
            AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled, UpdateMode = ReflectionProbe.UpdateModeEnum.Once
        });
        root.AddChild(new ReflectionProbe
        {
            Name = "Tower Guardian diamond reflections", Position = VoidAsheronPosition + new Vector3(-28, 61, 8),
            Size = new Vector3(410, 332, 400), MaxDistance = 1800, BlendDistance = 16,
            CullMask = battleScenery | VoidReflectionSceneryLayer,
            ReflectionMask = VoidGuardianDiamondLayer, Intensity = 2.1f,
            AmbientMode = ReflectionProbe.AmbientModeEnum.Disabled, UpdateMode = ReflectionProbe.UpdateModeEnum.Once
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

    private static ArrayMesh BuildVoidAmethystCluster()
    {
        using var mesh = new SurfaceTool();
        using var random = new RandomNumberGenerator { Seed = 0xA6E77157 };
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        mesh.SetSmoothGroup(uint.MaxValue);
        Stone(new(-.18f, 0, .06f), new(.16f, .1f, -.10f), .82f, 2.72f, -1.53f);
        Stone(new(.48f, -.27f, .35f), new(-.32f, .72f, -.48f), .53f, 1.87f, -1.13f);
        Stone(new(-.42f, -.48f, -.56f), new(.34f, 1.56f, .65f), .48f, 1.43f, -.77f);
        Stone(new(.25f, -.51f, -.54f), new(.52f, 2.51f, -.72f), .46f, 1.15f, -.76f);
        Stone(new(-.59f, -.78f, .45f), new(-.56f, .29f, .52f), .39f, .98f, -.62f);
        mesh.GenerateNormals();
        return mesh.Commit();

        void Stone(Vector3 center, Vector3 rotation, float width, float height, float depth)
        {
            const int sides = 6;
            var basis = Basis.FromEuler(rotation);
            var shoulders = new Vector3[sides];
            var hips = new Vector3[sides];
            var fractures = new Vector3[sides];
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.Tau / sides + random.RandfRange(-.16f, .16f);
                Vector3 edge = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * width * random.RandfRange(.74f, 1.2f);
                shoulders[side] = center + basis * (edge + Vector3.Up * (height * random.RandfRange(.42f, .69f)));
                hips[side] = center + basis * (edge * random.RandfRange(.76f, 1.28f) + Vector3.Up * (depth * random.RandfRange(.24f, .58f)));
                fractures[side] = center + basis * (edge * random.RandfRange(.47f, .85f) + Vector3.Up * (depth * random.RandfRange(.77f, 1.15f)));
            }
            Vector3 crown = center + basis * new Vector3(width * .24f, height, width * -.19f);
            Vector3 heel = center + basis * new Vector3(width * -.15f, depth * .98f, width * .2f);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                float tint = random.RandfRange(.44f, .94f);
                Face(crown, shoulders[side], shoulders[next], tint, 0, .96f);
                Face(shoulders[side], hips[side], shoulders[next], tint * .91f, .06f, .43f);
                Face(shoulders[next], hips[side], hips[next], tint * .82f, .12f, .25f);
                Face(hips[side], fractures[side], hips[next], tint * .67f, .62f, 0);
                Face(hips[next], fractures[side], fractures[next], tint * .58f, .85f, 0);
                Face(heel, fractures[next], fractures[side], tint * .44f, 1, 0);
            }

            void Face(Vector3 a, Vector3 b, Vector3 c, float tint, float fracture, float tip)
            {
                Vector3 outward = (a + b + c) / 3 - center;
                if ((c - a).Cross(b - a).Dot(outward) < 0) (b, c) = (c, b);
                mesh.SetColor(new Color(tint, tint, tint));
                mesh.SetUV2(new Vector2(fracture, tip));
                mesh.AddVertex(a);
                mesh.AddVertex(b);
                mesh.AddVertex(c);
            }
        }
    }
}
