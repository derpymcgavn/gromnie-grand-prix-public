using System.Text.Json;
using DerethSurvivors;
using Godot;
using System.IO;

public partial class GrandPrix
{
    private const int BrowserTilePixels = 32;
    private const string BrowserRouteName = "Yanshi XL";

    private void ExportYanshiForBrowser(string folder, string output)
    {
        var root = new Node3D { Name = "Yanshi XL" };
        AddChild(root);
        try
        {
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            using var portal = new DatArchive(Path.Combine(folder, "client_portal.dat"));
            using var cells = new DatArchive(Path.Combine(folder, "client_cell_1.dat"));
            using var reader = new AcAssets(portal, folder);
            var region = AcWorldData.ReadRegion(portal.Read(0x13000000));
            var town = ReadTownCatalog().OutdoorDestinations.Single(row => row.Name == "Yanshi");
            var routes = JsonSerializer.Deserialize<XlRoute[]>(
                Godot.FileAccess.GetFileAsString("res://Data/xl-routes.json")) ?? [];
            var xl = routes.Single(row => row.TownId == town.Id);
            var land = AcLandArena.Load(xl.Center, cells, region, 8);
            var circuit = new BogCircuit(BrowserRouteName, xl.Points(), land, xl.HalfWidth, gradedRoad: true);
            using var curve = circuit.Curve;
            BuildTownLand(root, land, reader, cells, circuit);
            if (!XlRoute.Validate(circuit, land, XlRoute.TownPosition(town, land), out var details))
                throw new InvalidDataException("Yanshi XL failed the native course validation.");

            var nativeBlend = region.Blend ?? throw new InvalidDataException("Yanshi has no terrain blend data.");
            var packedDirt = reader.Texture(nativeBlend.Textures.Single(layer => layer.Type == 7).Texture);
            var road = new StandardMaterial3D
            {
                ResourceName = "Packed dirt", AlbedoTexture = packedDirt, AlbedoColor = new Color(1, .88f, .75f),
                Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            circuit.BuildGradedRoad(root, road);
            circuit.BuildTownMarkers(root);
            var ground = new StandardMaterial3D
            {
                ResourceName = "Yanshi terrain composite", AlbedoTexture = BakeBrowserGround(land, reader, nativeBlend),
                Roughness = 1, CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };

            var materials = new Dictionary<StandardMaterial3D, StandardMaterial3D>();
            var meshes = new Dictionary<Mesh, Mesh>();
            int nodeCount = 0;
            foreach (var instance in BrowserMeshes(root))
            {
                if (instance.Mesh is not { } mesh || mesh.GetSurfaceCount() == 0) continue;
                nodeCount++;
                var bounds = land.WorldBounds;
                Vector2 GroundUv(Vector3 point) => (new Vector2(point.X, point.Z) - bounds.Position) / bounds.Size;
                if (instance.Name.ToString().StartsWith("DAT_Terrain_", StringComparison.Ordinal)
                    || instance.Name.ToString().Contains("feathered verge", StringComparison.Ordinal))
                {
                    instance.Mesh = BrowserUvMesh(mesh, GroundUv, ground);
                    instance.MaterialOverride = null;
                }
                else if (instance.MaterialOverride == road || mesh.SurfaceGetMaterial(0) == road)
                    instance.Mesh = BrowserUvMesh(mesh, point => new Vector2(point.X, point.Z) * .22f, road);
                else if (!meshes.TryGetValue(mesh, out var lightweight))
                {
                    lightweight = (Mesh)mesh.Duplicate();
                    for (int surface = 0; surface < lightweight.GetSurfaceCount(); surface++)
                        if (lightweight.SurfaceGetMaterial(surface) is StandardMaterial3D material)
                            lightweight.SurfaceSetMaterial(surface, BrowserSceneryMaterial(material, materials));
                    meshes.Add(mesh, lightweight);
                    instance.Mesh = lightweight;
                }
                else instance.Mesh = lightweight;
                if (instance.MaterialOverride is StandardMaterial3D overrideMaterial && overrideMaterial != road)
                    instance.MaterialOverride = BrowserSceneryMaterial(overrideMaterial, materials);
            }

            using var document = new GltfDocument();
            using var gltf = new GltfState();
            var imported = document.AppendFromScene(root, gltf);
            if (imported != Error.Ok) throw new IOException($"Could not create the browser scene ({imported}).");
            var saved = document.WriteToFilesystem(gltf, Path.Combine(output, "yanshi.glb"));
            if (saved != Error.Ok) throw new IOException($"Could not write the browser scene ({saved}).");

            int steps = Math.Max(4, Mathf.CeilToInt(circuit.Length / 2));
            var samples = new float[steps][];
            var laneHeights = new float[steps][];
            for (int index = 0; index < steps; index++)
            {
                float distance = BogCircuit.Grid + index * circuit.Length / steps;
                var point = circuit.Point(distance);
                samples[index] = [point.X, point.Y, point.Z];
                laneHeights[index] = Enumerable.Range(-2, 5)
                    .Select(lane => circuit.Surface(distance, circuit.HalfWidth * lane * .5f).Y).ToArray();
            }
            var route = new
            {
                name = BrowserRouteName, courseId = XlCourseOffset + town.Id, length = circuit.Length,
                trackWidth = circuit.HalfWidth * 2, landblocks = land.Blocks.Count, samples, laneHeights
            };
            System.IO.File.WriteAllText(Path.Combine(output, "route.json"), JsonSerializer.Serialize(route));
            GD.Print($"WEB COURSE PASS | {details.Length:F0} m | {land.Blocks.Count} native blocks | {nodeCount} meshes | {steps} route samples.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static IEnumerable<MeshInstance3D> BrowserMeshes(Node node)
    {
        if (node is MeshInstance3D mesh) yield return mesh;
        foreach (var child in node.GetChildren())
            foreach (var childMesh in BrowserMeshes(child))
                yield return childMesh;
    }

    private static StandardMaterial3D BrowserSceneryMaterial(StandardMaterial3D source,
        Dictionary<StandardMaterial3D, StandardMaterial3D> materials)
    {
        if (materials.TryGetValue(source, out var cached)) return cached;
        var material = (StandardMaterial3D)source.Duplicate();
        material.NormalEnabled = false;
        material.NormalTexture = null;
        materials.Add(source, material);
        return material;
    }

    private static ArrayMesh BrowserUvMesh(Mesh mesh, Func<Vector3, Vector2> projection, Material material)
    {
        var browser = new ArrayMesh();
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            var source = mesh.SurfaceGetArrays(surface);
            var vertices = source[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = vertices;
            arrays[(int)Mesh.ArrayType.Normal] = source[(int)Mesh.ArrayType.Normal];
            arrays[(int)Mesh.ArrayType.Index] = source[(int)Mesh.ArrayType.Index];
            arrays[(int)Mesh.ArrayType.TexUV] = vertices.Select(projection).ToArray();
            browser.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            browser.SurfaceSetMaterial(surface, material);
        }
        return browser;
    }

    private static ImageTexture BakeBrowserGround(AcLandArena land, AcAssets reader, AcWorldData.TerrainBlend blend)
    {
        var descriptions = blend.Textures.OrderBy(layer => layer.Type).ToArray();
        var layers = descriptions.Select(layer => ReadBrowserImage(reader, layer.Texture, 64)).ToArray();
        var masks = blend.Corner.Concat(blend.Side).Concat(blend.Road)
            .Select(mask => ReadBrowserImage(reader, mask.Texture, BrowserTilePixels)).ToArray();
        int side = land.GridSide * 8 * BrowserTilePixels;
        var pixels = new byte[side * side * 3];
        var origin = land.WorldBounds.Position;
        for (int row = 0; row < side / BrowserTilePixels; row++)
            for (int column = 0; column < side / BrowserTilePixels; column++)
            {
                var native = land.Resolve(origin.X + 24 * (column + .5f), origin.Y + 24 * (row + .5f));
                var recipe = AcTerrainBlend.SurfaceFor(native.Block, land.Region, (int)(native.X / 24), (int)(native.Y / 24));
                for (int y = 0; y < BrowserTilePixels; y++)
                    for (int x = 0; x < BrowserTilePixels; x++)
                    {
                        float u = (x + .5f) / BrowserTilePixels;
                        float v = (y + .5f) / BrowserTilePixels;
                        float a = Mask(recipe.Ovl0AlphaLayer, recipe.Ovl0Rotation, u, v);
                        float b = Mask(recipe.Ovl1AlphaLayer, recipe.Ovl1Rotation, u, v);
                        float c = Mask(recipe.Ovl2AlphaLayer, recipe.Ovl2Rotation, u, v);
                        Vector3 color = Layer(recipe.BaseLayer, u, v) * (a * b * c)
                            + Layer(recipe.Ovl0Layer, u, v) * (1 - a)
                            + Layer(recipe.Ovl1Layer, u, v) * (a * (1 - b))
                            + Layer(recipe.Ovl2Layer, u, v) * (b * (1 - c));
                        if (recipe.RoadLayer != AcTerrainBlend.Surface.None)
                        {
                            float road = 1 - Mask(recipe.Road0AlphaLayer, recipe.Road0Rotation, u, v)
                                * Mask(recipe.Road1AlphaLayer, recipe.Road1Rotation, u, v);
                            color = color.Lerp(Layer(recipe.RoadLayer, u, v), road);
                        }
                        int index = ((row * BrowserTilePixels + y) * side + column * BrowserTilePixels + x) * 3;
                        pixels[index] = (byte)Math.Clamp(color.X * .90f, 0, 255);
                        pixels[index + 1] = (byte)Math.Clamp(color.Y * .90f, 0, 255);
                        pixels[index + 2] = (byte)Math.Clamp(color.Z * .90f, 0, 255);
                    }
            }
        using var image = Image.CreateFromData(side, side, false, Image.Format.Rgb8, pixels);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);

        float Mask(byte channel, byte rotation, float u, float v)
        {
            if (channel == AcTerrainBlend.Surface.None) return 1;
            (u, v) = rotation switch { 1 => (1 - v, u), 2 => (1 - u, 1 - v), 3 => (v, 1 - u), _ => (u, v) };
            int x = Math.Min(BrowserTilePixels - 1, (int)(u * BrowserTilePixels));
            int y = Math.Min(BrowserTilePixels - 1, (int)(v * BrowserTilePixels));
            return masks[channel][(y * BrowserTilePixels + x) * 4 + 3] / 255f;
        }

        Vector3 Layer(byte channel, float u, float v)
        {
            if (channel == AcTerrainBlend.Surface.None) return Vector3.Zero;
            var texture = layers[channel];
            int tiling = checked((int)descriptions[channel].Tiling);
            int x = (int)(u * tiling * 64) % 64;
            int y = (int)(v * tiling * 64) % 64;
            var index = (y * 64 + x) * 4;
            return new Vector3(texture[index], texture[index + 1], texture[index + 2]);
        }
    }

    private static byte[] ReadBrowserImage(AcAssets reader, uint id, int size)
    {
        var texture = reader.Texture(id) ?? throw new InvalidDataException($"Missing Yanshi texture {id:X8}.");
        using var image = texture.GetImage();
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
            throw new InvalidDataException($"Could not read Yanshi texture {id:X8}.");
        image.ClearMipmaps();
        image.Convert(Image.Format.Rgba8);
        image.Resize(size, size, Image.Interpolation.Bilinear);
        return image.GetData();
    }
}
