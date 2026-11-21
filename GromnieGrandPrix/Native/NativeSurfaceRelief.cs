using Godot;

namespace DerethSurvivors;

internal static class NativeSurfaceRelief
{
    // Make a small normal map from the texture's luminance, keeping the DAT
    // albedo and its palette intact. Rounded gradients prevent hard edges on dyes.
    public static Texture2D? FromTexture(Texture2D texture)
    {
        using var image = texture.GetImage();
        if (image is null || image.IsEmpty() || image.IsCompressed() && image.Decompress() != Error.Ok) return null;
        image.ClearMipmaps();
        int width = image.GetWidth(), height = image.GetHeight();
        if (Math.Min(width, height) < 4) return null;
        float ratio = Math.Min(1, 128f / Math.Max(width, height));
        if (ratio < 1)
        {
            width = Math.Max(4, Mathf.RoundToInt(width * ratio));
            height = Math.Max(4, Mathf.RoundToInt(height * ratio));
            image.Resize(width, height, Image.Interpolation.Lanczos);
        }
        image.Convert(Image.Format.Rgba8);

        byte[] rgba = image.GetData();
        float[] luminance = new float[width * height];
        for (int i = 0, j = 0; i < luminance.Length; i++, j += 4)
            luminance[i] = (rgba[j] * .2126f + rgba[j + 1] * .7152f + rgba[j + 2] * .0722f) / 255;

        var normals = new byte[luminance.Length * 3];
        bool hasRelief = false;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, prev = ((y + height - 1) % height) * width, next = ((y + 1) % height) * width;
            for (int x = 0; x < width; x++)
            {
                int left = (x + width - 1) % width, right = (x + 1) % width;
                float dx = luminance[row + right] - luminance[row + left] +
                    .25f * (luminance[prev + right] + luminance[next + right] - luminance[prev + left] - luminance[next + left]);
                float dy = luminance[next + x] - luminance[prev + x] +
                    .25f * (luminance[next + left] + luminance[next + right] - luminance[prev + left] - luminance[prev + right]);
                hasRelief |= Math.Abs(dx) + Math.Abs(dy) > .012f;
                var normal = new Vector3(Mathf.Clamp(-dx * 1.7f, -.48f, .48f), Mathf.Clamp(dy * 1.7f, -.48f, .48f), 1).Normalized();
                int index = (row + x) * 3;
                normals[index] = Pack(normal.X);
                normals[index + 1] = Pack(normal.Y);
                normals[index + 2] = Pack(normal.Z);
            }
        }
        if (!hasRelief) return null;
        using var normalImage = Image.CreateFromData(width, height, false, Image.Format.Rgb8, normals);
        normalImage.GenerateMipmaps(renormalize: true);
        return ImageTexture.CreateFromImage(normalImage);
    }

    private static byte Pack(float value) => (byte)Mathf.RoundToInt((value * .5f + .5f) * 255);

    public static float[] Tangents(Vector3[] positions, Vector3[] normals, Vector2[] uvs)
    {
        var tangents = new float[positions.Length * 4];
        for (int i = 0; i < positions.Length; i += 3)
        {
            Vector3 edgeA = positions[i + 1] - positions[i], edgeB = positions[i + 2] - positions[i];
            Vector2 uvA = uvs[i + 1] - uvs[i], uvB = uvs[i + 2] - uvs[i];
            float orientation = Math.Sign(uvA.Cross(uvB));
            Vector3 tangent = (edgeA * uvB.Y - edgeB * uvA.Y) * orientation;
            Vector3 bitangent = (edgeB * uvA.X - edgeA * uvB.X) * orientation;
            for (int corner = i; corner < i + 3; corner++)
            {
                Vector3 normal = normals[corner].Normalized();
                Vector3 t = tangent.Slide(normal);
                if (t.LengthSquared() < 1e-12f)
                    t = normal.Cross(Math.Abs(normal.Y) < .97f ? Vector3.Up : Vector3.Right);
                t = t.Normalized();
                int at = corner * 4;
                tangents[at] = t.X;
                tangents[at + 1] = t.Y;
                tangents[at + 2] = t.Z;
                tangents[at + 3] = normal.Cross(t).Dot(bitangent) < 0 ? -1 : 1;
            }
        }
        return tangents;
    }
}
