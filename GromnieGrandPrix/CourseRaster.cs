using Godot;
using DerethSurvivors;

internal static class CourseRaster
{
    public static float[] SquaredDistances(BogCircuit track, Vector2 origin, Vector2 extent, int size, float radius) =>
        Rasterize(track, origin, extent, size, radius, roadEdges: false);

    private static float[] Rasterize(BogCircuit track, Vector2 origin, Vector2 extent, int size, float radius, bool roadEdges)
    {
        var distances = new float[size * size];
        Array.Fill(distances, roadEdges ? radius - track.HalfWidth : radius * radius);
        Vector2 pixel = extent / size;
        int count = Mathf.CeilToInt(track.Length / 1.1f);
        Vector2 a = MuckwaterLayout.Flat(track.Curve.SampleBaked(0, true));
        float beforeWidth = track.WidthAt(0);
        for (int sample = 1; sample <= count; sample++)
        {
            float distance = sample * track.Length / count;
            Vector2 b = MuckwaterLayout.Flat(track.Curve.SampleBaked(distance, true));
            float width = roadEdges ? track.WidthAt(distance) : beforeWidth;
            if (roadEdges && track.SpanDeck((sample - .5f) * track.Length / count) is not null)
            {
                a = b;
                beforeWidth = width;
                continue;
            }
            Vector2 delta = b - a;
            float lengthSquared = delta.LengthSquared();
            if (lengthSquared <= .0001f) continue;
            Vector2 pad = Vector2.One * (roadEdges ? radius - track.HalfWidth + Math.Max(beforeWidth, width) : radius);
            Vector2 min = (a.Min(b) - pad - origin) / pixel;
            Vector2 max = (a.Max(b) + pad - origin) / pixel;
            int left = Math.Max(0, Mathf.FloorToInt(min.X)), right = Math.Min(size - 1, Mathf.CeilToInt(max.X));
            int bottom = Math.Max(0, Mathf.FloorToInt(min.Y)), top = Math.Min(size - 1, Mathf.CeilToInt(max.Y));
            for (int z = bottom; z <= top; z++)
                for (int x = left; x <= right; x++)
                {
                    Vector2 point = origin + new Vector2(x + .5f, z + .5f) * pixel;
                    float t = Mathf.Clamp((point - a).Dot(delta) / lengthSquared, 0, 1);
                    int texel = z * size + x;
                    float squared = point.DistanceSquaredTo(a + delta * t);
                    float gap = roadEdges ? Mathf.Sqrt(squared) - Mathf.Lerp(beforeWidth, width, t) : squared;
                    distances[texel] = Math.Min(distances[texel], gap);
                }
            a = b;
            beforeWidth = width;
        }
        return distances;
    }

    public static Texture2D TownMask(BogCircuit track)
    {
        float width = track.HalfWidth;
        Vector2 origin = track.NativeBounds.Position, extent = track.NativeBounds.Size;
        int size = track.IsSprint ? 2304 : Mathf.Clamp(Mathf.RoundToInt(extent.X * 1.25f), 256, 1792);
        var distances = Rasterize(track, origin, extent, size, width + 8.2f, roadEdges: track.IsSprint);
        Vector2 pixel = extent / size;
        var rgba = new byte[distances.Length * 4];
        for (int i = 0; i < distances.Length; i++)
        {
            float edge = track.IsSprint ? distances[i] : Mathf.Sqrt(distances[i]) - width;
            rgba[i * 4] = Pack(1 - Mathf.SmoothStep(-1.6f, 3.9f, edge));
            rgba[i * 4 + 2] = Pack(1 - Mathf.SmoothStep(1.2f, 7.8f, edge));
            if (edge < 3.9f)
            {
                Vector2 point = origin + new Vector2(i % size + .5f, i / size + .5f) * pixel;
                Vector3 at = new(point.X, 0, point.Y);
                const float drainageRadius = 5;
                float rim = (track.FreeGround(at + Vector3.Right * drainageRadius).Y + track.FreeGround(at + Vector3.Left * drainageRadius).Y
                    + track.FreeGround(at + Vector3.Forward * drainageRadius).Y + track.FreeGround(at + Vector3.Back * drainageRadius).Y) * .25f;
                rgba[i * 4 + 3] = Pack(.5f + (rim - track.FreeGround(at).Y) * 1.45f);
            }
        }
        using var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static byte Pack(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp(value, 0, 1) * 255);
}

