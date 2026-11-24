using Godot;
using DerethSurvivors;
using System.Runtime.InteropServices;

public partial class GrandPrix
{
    private const int IslandWaterCourseId = 13009;
    private const string IslandWaterCourseName = "Asheron's Island Regatta";
    private const uint IslandCenterBlock = 0xD798FFFF;
    private const uint IslandReflectionLayer = 1u << 6;
    private const float IslandSeaHeight = .14f;
    private MeshInstance3D? islandSea;
    private SubViewport? islandReflection;
    private Camera3D? islandMirrorCamera;
    private const int RegattaFluidTextureSide = 152;
    private float regattaFluidUploadedAt = float.NegativeInfinity;
    private float[]? regattaFluidHeights;
    private ushort[]? regattaFluidHalf;
    private ImageTexture? regattaFluidTexture;
    private static readonly uint[] IslandBlocks = (from east in Enumerable.Range(0xCE, 18)
        from north in Enumerable.Range(0x8F, 18)
        select ((uint)east << 24) | ((uint)north << 16) | 0xFFFFu).ToArray();
    private static readonly Vector2[] IslandCourseAnchors =
    [
        new(-1052, -348), new(-1044, -284), new(-992, -76), new(-900, 140),
        new(-720, 360), new(-560, 576), new(-452, 840), new(-300, 1100),
        new(-152, 1324), new(80, 1460), new(376, 1488), new(672, 1432),
        new(876, 1260), new(996, 1032), new(1076, 768), new(1108, 460),
        new(1124, 160), new(1132, -52), new(1016, -192), new(840, -248),
        new(724, -420), new(572, -624), new(384, -788), new(180, -916),
        new(-36, -1032), new(-252, -1104), new(-504, -1108), new(-748, -1040),
        new(-936, -908), new(-1028, -720), new(-1064, -596),
    ];

    private bool IslandWaterCourseAvailable => townCells is { } cells && townRegion is not null &&
        IslandBlocks.All(cells.Entries.ContainsKey);
    private Vector3 IslandShowAnchor { get; set; }

    private BogCircuit BuildIslandWaterCourse(Node3D scene)
    {
        var land = AcLandArena.LoadCorridor(IslandCenterBlock, IslandBlocks, townCells!, townRegion!);
        float water = IslandSeaHeight - land.ElevationOrigin;
        var route = new BogCircuit(IslandWaterCourseName, IslandCourseAnchors, land, 24, waterCourseLevel: water);
        try
        {
            float[] channelLanes = [-28f, 0, 28f];
            for (float at = 0; at < route.Length; at += 9)
            {
                foreach (float lane in channelLanes)
                {
                    Vector3 point = route.Surface(at, lane);
                    if (!land.ContainsGround(point.X, point.Z) ||
                        !land.TerrainName(point.X, point.Z).Contains("Water", StringComparison.OrdinalIgnoreCase) ||
                        land.Ground(point.X, point.Z).Y > water)
                        throw new InvalidDataException("The regatta's channel is blocked in this copy of the Asheron's Island DAT.");
                }
            }

            // The sea sheet covers empty ocean blocks. Keep real coastline geometry and the castle.
            bool RenderCoast(AcWorldData.Block block) => block.Id == IslandCenterBlock || block.HasObjects ||
                block.Heights.Any(height => land.Region.Heights[height] > IslandSeaHeight);
            var routeSamples = Enumerable.Range(0, Mathf.CeilToInt(route.Length / 105))
                .Select(index => MuckwaterLayout.Flat(route.Point(index * 105))).ToArray();
            land.Build(scene, assets, townCells!, (point, _, _) =>
                routeSamples.Any(position => point.DistanceSquaredTo(position) < 330 * 330) &&
                point.DistanceSquaredTo(new Vector2(-976, -312)) > 22 * 22, renderBlock: RenderCoast, visualWaterLevel: water);
            AcTerrainRenderer.For(assets, land.Region).Material.SetShaderParameter("course_mask", default(Variant));
            IslandShowAnchor = land.Ground(-976, -312);
            BuildIslandBuoys(scene, route);
            BuildIslandWater(scene, land, water);
            GD.Print($"{IslandWaterCourseName}: {route.Length:F0} m with {land.Blocks.Values.Count(RenderCoast)} rendered native blocks and a floating channel.");
            return route;
        }
        catch
        {
            route.Curve.Dispose();
            throw;
        }
    }

    private void BuildIslandWater(Node3D scene, AcLandArena land, float level, bool nativeWaterOnly = false)
    {
        StopIslandWaterReflection();
        islandReflection = null;
        islandMirrorCamera = null;
        regattaFluidUploadedAt = float.NegativeInfinity;
        RenderingServer.GlobalShaderParameterSet("regatta_fluid_bounds", Vector4.Zero);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/IslandRegattaWater.gdshader") };
        RegattaSurface.Configure(material);
        // Use the retail region's actual shallow, open-water and river art for
        // moving wave, seabed, and broken foam detail.
        SetNative("native_water", "WaterShallowSea");
        SetNative("native_deep_water", "WaterDeepSea");
        SetNative("native_surf", "WaterRunning");
        SetNative("native_sand", "sand-yellow");
        material.SetShaderParameter("shore_distance", BuildIslandShoreDistance(land, level, nativeWaterOnly));
        material.SetShaderParameter("native_water_only", nativeWaterOnly);
        material.SetShaderParameter("shore_origin", land.WorldBounds.Position);
        material.SetShaderParameter("shore_extent", land.WorldBounds.Size);
        bool desktopReflection = !OS.HasFeature("android") && DisplayServer.GetName() != "headless";
        if (desktopReflection) AddIslandReflectionLayers(scene);
        Vector2 center = land.WorldBounds.GetCenter();
        islandSea = BogCircuit.MeshAt(scene, BuildRegattaSeaMesh(),
            new Vector3(center.X, level, center.Y), Vector3.One, material);
        islandSea.Name = "Regatta sea and shoals";
        islandSea.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        islandSea.ExtraCullMargin = 1.15f;
        if (!desktopReflection) return;

        islandReflection = new SubViewport
        {
            Name = "Regatta planar reflection",
            World3D = GetViewport().FindWorld3D(),
            Size = new Vector2I(416, 256),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };
        scene.AddChild(islandReflection);
        islandMirrorCamera = new Camera3D { CullMask = IslandReflectionLayer, Current = true };
        islandReflection.AddChild(islandMirrorCamera);

        void SetNative(string sampler, string name)
        {
            uint id = land.Region.Terrains.First(terrain => terrain.Name == name).Texture;
            if (assets.Texture(id) is { } texture) material.SetShaderParameter(sampler, texture);
        }
    }

    private static ArrayMesh BuildRegattaSeaMesh()
    {
        // Keep the waves under the racers finely tessellated. Coarse cells
        // cover the rest of the visible ocean with no patch seams.
        const float cell = RegattaFluidField.CellSize;
        var distances = new List<float> { 0 };
        // Match the disturbance grid around the camera. Its world-space phase does not move when this mesh recenters.
        for (int i = 1; i <= 55; i++) distances.Add(i * cell);
        for (int i = 1; i <= 12; i++) distances.Add((55 + 2 * i) * cell);
        for (int i = 1; i <= 16; i++) distances.Add(79 * cell + i * 4);
        for (float distance = 128, step = 8; distance < 3800; step *= 1.65f)
        {
            distance += step;
            distances.Add(distance);
        }
        float[] axis = distances.Skip(1).Reverse().Select(value => -value).Concat(distances).ToArray();
        int side = axis.Length;
        var vertices = new Vector3[side * side];
        var normals = new Vector3[vertices.Length];
        var indices = new int[(side - 1) * (side - 1) * 6];
        int index = 0;
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            int point = z * side + x;
            vertices[point] = new Vector3(axis[x], 0, axis[z]);
            normals[point] = Vector3.Up;
            if (x == side - 1 || z == side - 1) continue;
            indices[index++] = point;
            indices[index++] = point + 1;
            indices[index++] = point + side;
            indices[index++] = point + 1;
            indices[index++] = point + side + 1;
            indices[index++] = point + side;
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private void SetRegattaWaterTime(float time)
    {
        if (GodotObject.IsInstanceValid(islandSea) && islandSea!.MaterialOverride is ShaderMaterial surface)
            surface.SetShaderParameter("regatta_time", time);
    }

    private void SetRegattaFluidSurface(RegattaFluidField field, Vector3 viewAnchor, bool force = false)
    {
        if (DisplayServer.GetName() == "headless") return;
        float time = RegattaWaterTime;
        if (!force && time >= regattaFluidUploadedAt && time - regattaFluidUploadedAt < .030f) return;
        regattaFluidUploadedAt = time;

        const int size = RegattaFluidTextureSide;
        const float cell = RegattaFluidField.CellSize;
        int x = Mathf.RoundToInt(viewAnchor.X / cell) - size / 2;
        int z = Mathf.RoundToInt(viewAnchor.Z / cell) - size / 2;
        var heights = regattaFluidHeights ??= new float[size * size];
        var packed = regattaFluidHalf ??= new ushort[size * size];
        field.CopyHeightPatch(x, z, size, size, heights);
        for (int i = 0; i < heights.Length; i++)
            packed[i] = BitConverter.HalfToUInt16Bits((Half)heights[i]);

        // R16F keeps the signs of water heights while avoiding a full floating-point RGBA upload.
        using var image = Image.CreateFromData(size, size, false, Image.Format.Rh, MemoryMarshal.AsBytes(packed.AsSpan()));
        if (regattaFluidTexture is null)
        {
            regattaFluidTexture = ImageTexture.CreateFromImage(image);
            RenderingServer.GlobalShaderParameterSet("regatta_fluid_displacement", regattaFluidTexture);
        }
        else regattaFluidTexture.Update(image);
        RenderingServer.GlobalShaderParameterSet("regatta_fluid_bounds",
            new Vector4(x * cell, z * cell, 1 / (size * cell), 1f / size));
    }

    private static void AddIslandReflectionLayers(Node3D scene)
    {
        foreach (var geometry in scene.FindChildren("*", nameof(GeometryInstance3D), true, false)
            .OfType<GeometryInstance3D>())
            geometry.Layers |= IslandReflectionLayer;
    }

    private void UpdateIslandWaterReflection()
    {
        if (!GodotObject.IsInstanceValid(islandSea) || islandSea!.MaterialOverride is not ShaderMaterial surface)
            return;
        if (!track.HasWaterSections || !racing || finished)
        {
            StopIslandWaterReflection();
            return;
        }
        surface.SetShaderParameter("sunlight", weatherSun.LightColor);
        surface.SetShaderParameter("sun_energy", weatherSun.LightEnergy);
        Vector3 eye = camera.GlobalPosition;
        float level = islandSea.GlobalPosition.Y;
        // Recenter the detail grid, while the wave phase stays fixed in world space.
        const float gridStep = 8;
        var center = new Vector3(Mathf.Floor(eye.X / gridStep + .5f) * gridStep, level,
            Mathf.Floor(eye.Z / gridStep + .5f) * gridStep);
        if (islandSea.GlobalPosition != center) islandSea.GlobalPosition = center;
        if (islandReflection is not { } reflection || islandMirrorCamera is not { } mirror) return;
        if (eye.Y <= level + .08f)
        {
            StopIslandWaterReflection();
            return;
        }

        Vector2 extent = GetViewport().GetVisibleRect().Size;
        int width = Mathf.Clamp((int)(extent.X * .31f), 320, 620);
        Vector2I target = new(width, Math.Max(1, Mathf.RoundToInt(width * extent.Y / Math.Max(extent.X, 1))));
        if (reflection.Size != target) reflection.Size = target;
        mirror.Projection = camera.Projection;
        mirror.Fov = camera.Fov;
        mirror.Size = camera.Size;
        mirror.Near = camera.Near;
        mirror.Far = Math.Min(camera.Far, 900);
        mirror.KeepAspect = camera.KeepAspect;
        Basis view = camera.GlobalBasis;
        surface.SetShaderParameter("reflection_eye", eye);
        eye.Y = 2 * level - eye.Y;
        mirror.GlobalTransform = new Transform3D(new Basis(Reflect(view.X), -Reflect(view.Y), Reflect(view.Z)), eye);
        if (reflection.RenderTargetUpdateMode == SubViewport.UpdateMode.Always) return;
        surface.SetShaderParameter("island_reflection", reflection.GetTexture());
        surface.SetShaderParameter("reflection_active", true);
        reflection.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

        static Vector3 Reflect(Vector3 axis) => new(axis.X, -axis.Y, axis.Z);
    }

    private void StopIslandWaterReflection()
    {
        if (!GodotObject.IsInstanceValid(islandReflection) ||
            islandReflection!.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled) return;
        islandReflection.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (!GodotObject.IsInstanceValid(islandSea) || islandSea!.MaterialOverride is not ShaderMaterial surface) return;
        surface.SetShaderParameter("reflection_active", false);
        surface.SetShaderParameter("island_reflection", default(Variant));
    }

    private static ImageTexture BuildIslandShoreDistance(AcLandArena land, float level, bool nativeWaterOnly)
    {
        const int size = 448;
        const float diagonal = 1.41421356f, shoreRange = 96;
        Vector2 origin = land.WorldBounds.Position, pixel = land.WorldBounds.Size / size;
        var distance = new float[size * size];
        var wet = nativeWaterOnly ? new byte[size * size] : null;
        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
        {
            Vector2 point = origin + (new Vector2(x, z) + Vector2.One * .5f) * pixel;
            int i = z * size + x;
            bool submerged = land.Ground(point.X, point.Y).Y <= level;
            if (wet is not null)
            {
                submerged &= land.ContainsGround(point.X, point.Y) &&
                    land.TerrainName(point.X, point.Y).Contains("Water", StringComparison.OrdinalIgnoreCase);
                wet[i] = submerged ? (byte)255 : (byte)0;
            }
            distance[i] = submerged ? size : 0;
        }
        for (int z = 1; z < size - 1; z++)
        for (int x = 1; x < size - 1; x++)
        {
            int i = z * size + x;
            distance[i] = Math.Min(distance[i], Math.Min(
                Math.Min(distance[i - 1], distance[i - size]) + 1,
                Math.Min(distance[i - size - 1], distance[i - size + 1]) + diagonal));
        }
        for (int z = size - 2; z >= 1; z--)
        for (int x = size - 2; x >= 1; x--)
        {
            int i = z * size + x;
            distance[i] = Math.Min(distance[i], Math.Min(
                Math.Min(distance[i + 1], distance[i + size]) + 1,
                Math.Min(distance[i + size - 1], distance[i + size + 1]) + diagonal));
        }
        int channels = wet is null ? 1 : 4;
        var packed = new byte[distance.Length * channels];
        for (int i = 0; i < distance.Length; i++)
        {
            packed[i * channels] = (byte)Mathf.RoundToInt(255 * Math.Min(1, distance[i] * pixel.X / shoreRange));
            if (wet is not null)
            {
                packed[i * channels + 1] = wet[i];
                packed[i * channels + 3] = 255;
            }
        }
        using var image = Image.CreateFromData(size, size, false, wet is null ? Image.Format.R8 : Image.Format.Rgba8, packed);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private void BuildIslandBuoys(Node3D scene, BogCircuit route)
    {
        var root = new Node3D { Name = "Regatta channel marker buoys" };
        scene.AddChild(root);
        int count = Mathf.CeilToInt(route.Length / 79);
        var floatMesh = new CylinderMesh { TopRadius = .81f, BottomRadius = 1.16f, Height = .63f, RadialSegments = 12 };
        var mastMesh = new CylinderMesh { TopRadius = .25f, BottomRadius = .38f, Height = 1.25f, RadialSegments = 10 };
        var lanternMesh = new SphereMesh { Radius = .28f, Height = .56f, RadialSegments = 10, Rings = 5 };
        // Riveted Gear Knight plating keeps the improvised channel markers in
        // the same visual world as the skis.
        var basePlate = assets.Surface(0x08001558).AlbedoTexture;
        var mastPlate = RegattaPlateTexture();
        var capPlate = assets.Surface(0x08001562).AlbedoTexture;
        foreach (int side in new[] { -1, 1 })
        {
            float lane = (route.HalfWidth + 2.2f) * side;
            var stations = Enumerable.Range(0, count).Select(i => route.Surface(35 + i * route.Length / count, lane)).ToArray();
            string paint = side < 0 ? "fd653e" : "48e4bd";
            var body = RegattaFinish(basePlate, paint, .39f);
            var pole = RegattaFinish(mastPlate, "fff1d1", .25f);
            var light = RegattaFinish(capPlate, paint, .32f, glow: .63f);
            AddBuoyRow("Floating base", floatMesh, body, .21f);
            AddBuoyRow("Reflective mast", mastMesh, pole, .97f);
            AddBuoyRow("Channel lantern", lanternMesh, light, 1.69f);

            void AddBuoyRow(string name, Mesh mesh, Material finish, float height)
            {
                var instances = new MultiMesh
                {
                    Mesh = mesh, TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = stations.Length
                };
                for (int i = 0; i < stations.Length; i++)
                    instances.SetInstanceTransform(i, new Transform3D(Basis.Identity, stations[i] + Vector3.Up * height));
                root.AddChild(new MultiMeshInstance3D
                {
                    Name = $"{name} {(side < 0 ? "red" : "green")}", Multimesh = instances, MaterialOverride = finish,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                });
            }
        }
    }
}
