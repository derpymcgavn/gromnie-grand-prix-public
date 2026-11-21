using Godot;
using DerethSurvivors;

// The road's distance field supplies both the render mask and scenery clearance.
internal sealed class MuckwaterLayout
{
    public const float WaterLevel = -.42f;
    private const int Size = 624;
    private const float Cell = 12;
    private const float SearchRadius = 34;
    public static readonly Vector2 Origin = new(-258, -234);
    public static readonly Vector2 Extent = new(434, 458);
    public static readonly (Vector2 Center, Vector2 Radius)[] Ponds =
    [
        (new(-21, 23), new(30, 47)),
        (new(-89, -86), new(37, 27)),
        (new(-104, 69), new(24, 29))
    ];

    private readonly float[] road;
    private readonly float roadHalf;
    private static readonly Vector2 Pixel = Extent / Size;
    private readonly FastNoiseLite relief = new() { Seed = 144, Frequency = .034f, FractalOctaves = 3 };
    private readonly FastNoiseLite banks = new() { Seed = 324, Frequency = .076f, FractalOctaves = 2 };

    public MuckwaterLayout(BogCircuit track)
    {
        roadHalf = track.HalfWidth;
        road = CourseRaster.SquaredDistances(track, Origin, Extent, Size, SearchRadius);
    }

    private static Vector2 AtPixel(int x, int z) => Origin + new Vector2(x + .5f, z + .5f) * Pixel;
    public static Vector2 Flat(Vector3 point) => new(point.X, point.Z);

    public float RoadDistance(Vector2 point)
    {
        Vector2 pixel = (point - Origin) / Pixel - Vector2.One * .5f;
        int x = Mathf.Clamp(Mathf.FloorToInt(pixel.X), 0, Size - 2);
        int y = Mathf.Clamp(Mathf.FloorToInt(pixel.Y), 0, Size - 2);
        float u = Mathf.Clamp(pixel.X - x, 0, 1), v = Mathf.Clamp(pixel.Y - y, 0, 1);
        int i = y * Size + x;
        return Mathf.Lerp(
            Mathf.Lerp(Mathf.Sqrt(road[i]), Mathf.Sqrt(road[i + 1]), u),
            Mathf.Lerp(Mathf.Sqrt(road[i + Size]), Mathf.Sqrt(road[i + Size + 1]), u), v);
    }

    public float PondDistance(Vector2 point)
    {
        float nearest = float.MaxValue;
        foreach (var pond in Ponds)
        {
            float scaledDistance = ((point - pond.Center) / pond.Radius).Length();
            nearest = Math.Min(nearest, (scaledDistance - 1) * Math.Min(pond.Radius.X, pond.Radius.Y));
        }
        return nearest + banks.GetNoise2D(point.X, point.Y) * 2.4f;
    }

    public float Height(Vector2 point)
    {
        float d = RoadDistance(point);
        // Keep the full driveable area on the race simulation's original plane.
        if (d <= roadHalf + 2.7f) return -.035f;
        float distance = PondDistance(point);
        float shoulder = Mathf.SmoothStep(roadHalf + 2.7f, roadHalf + 8.3f, d);
        float variation = Mathf.SmoothStep(-1, 5, distance);
        float far = Mathf.SmoothStep(12, SearchRadius, d);
        float elevation = relief.GetNoise2D(point.X, point.Y);
        Vector2 fromEdge = (point - Origin).Min(Origin + Extent - point);
        float rim = 1 - Mathf.SmoothStep(5, 36, Math.Min(fromEdge.X, fromEdge.Y));
        float terrain = Math.Max(-.24f, elevation * (.7f + 3.1f * far) + .38f * far);
        terrain += rim * (3.2f + 7.6f * (elevation + .55f));
        float basin = 1.2f * (1 - Mathf.SmoothStep(-5, 1.3f, distance));
        return -.035f + shoulder * (variation * terrain - basin);
    }

    public Vector3 OnGround(Vector2 point, float lift = 0) => new(point.X, Height(point) + lift, point.Y);

    public ImageTexture BuildMask(AcAssets assets, AcWorldData.TerrainBlend region, uint grass, uint marsh)
    {
        var alphaTextures = region.Corner.Concat(region.Side).Select(map => ReadMask(assets, map.Texture)).ToArray();
        try
        {
            int cols = Mathf.CeilToInt(Extent.X / Cell), rows = Mathf.CeilToInt(Extent.Y / Cell);
            var corners = new int[cols + 1, rows + 1];
            for (int x = 0; x <= cols; x++)
                for (int z = 0; z <= rows; z++)
                {
                    Vector2 at = Origin + new Vector2(x, z) * Cell;
                    bool wet = PondDistance(at) - 5 + relief.GetNoise2D(at.X, at.Y) * 16 < 0;
                    corners[x, z] = wet ? (int)marsh : (int)grass;
                }

            var tiles = new AcTerrainBlend.Surface[cols, rows];
            for (int x = 0; x < cols; x++)
                for (int z = 0; z < rows; z++)
                {
                    uint code = AcTerrainBlend.GetPalCode(0, 0, 0, 0,
                        corners[x, z + 1], corners[x + 1, z + 1], corners[x + 1, z], corners[x, z]);
                    tiles[x, z] = AcTerrainBlend.BuildSurface(code, region);
                }

            var bytes = new byte[Size * Size * 4];
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    Vector2 point = AtPixel(x, z);
                    Vector2 tile = (point - Origin) / Cell;
                    int tx = (int)tile.X, tz = (int)tile.Y;
                    Vector2 uv = tile - new Vector2(tx, tz);
                    var recipe = tiles[tx, tz];
                    float baseWeight = 1;
                    baseWeight *= SampleInverseAlpha(alphaTextures, recipe.Ovl0AlphaLayer, recipe.Ovl0Rotation, uv);
                    baseWeight *= SampleInverseAlpha(alphaTextures, recipe.Ovl1AlphaLayer, recipe.Ovl1Rotation, uv);
                    baseWeight *= SampleInverseAlpha(alphaTextures, recipe.Ovl2AlphaLayer, recipe.Ovl2Rotation, uv);
                    float marshWeight = recipe.BaseLayer == marsh ? baseWeight : 1 - baseWeight;
                    float variation = banks.GetNoise2D(point.X, point.Y);
                    float distance = Mathf.Sqrt(road[z * Size + x]);
                    float wetness = 1 - Mathf.SmoothStep(-1.7f, 4.2f, PondDistance(point));
                    int i = (z * Size + x) * 4;
                    bytes[i] = Pack(1 - Mathf.SmoothStep(roadHalf - .95f, roadHalf + .45f, distance + variation * .62f));
                    bytes[i + 1] = Pack(Math.Max(wetness, 1 - Mathf.SmoothStep(roadHalf + .4f, roadHalf + 2.75f, distance + variation * 1.1f)));
                    bytes[i + 2] = Pack(marshWeight);
                    bytes[i + 3] = Pack(Mathf.Clamp((WaterLevel - Height(point) + .20f) / 1.14f, 0, 1));
                }
            using var mask = Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, bytes);
            mask.GenerateMipmaps();
            return ImageTexture.CreateFromImage(mask);
        }
        finally
        {
            foreach (var alpha in alphaTextures) alpha.Dispose();
        }
    }

    private static Image ReadMask(AcAssets assets, uint id)
    {
        var image = (assets.Texture(id) ?? throw new InvalidDataException($"Missing AC alpha mask {id:X8}.")).GetImage();
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
        {
            image.Dispose();
            throw new InvalidDataException($"Cannot decompress AC alpha mask {id:X8}.");
        }
        return image;
    }

    private static float SampleInverseAlpha(Image[] masks, byte index, byte rotation, Vector2 p)
    {
        if (index == AcTerrainBlend.Surface.None) return 1;
        Vector2 uv = rotation switch
        {
            1 => new(1 - p.Y, p.X), 2 => Vector2.One - p, 3 => new(p.Y, 1 - p.X), _ => p
        };
        var image = masks[index];
        int x = Mathf.Clamp((int)(uv.X * image.GetWidth()), 0, image.GetWidth() - 1);
        int y = Mathf.Clamp((int)(uv.Y * image.GetHeight()), 0, image.GetHeight() - 1);
        return image.GetPixel(x, y).A;
    }

    private static byte Pack(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp(value, 0, 1) * 255);
}



