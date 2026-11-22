using Godot;
using System.Buffers.Binary;

namespace DerethSurvivors;

public static class DatTextures
{
    public static Texture2D? LoadIcon(DatArchive dat, uint id) => Load(dat, id);
    public static Texture2D? Load(DatArchive dat, uint id, uint palette = 0, DatArchive? paletteArchive = null, bool clipMap = false, IReadOnlyDictionary<int, uint>? paletteColors = null,bool preferEmbeddedPalette=false)
    {
        if (!dat.Entries.ContainsKey(id)) return null;
        var bytes = dat.Read(id);
        return Decode(bytes,paletteArchive??dat,palette,clipMap,paletteColors,preferEmbeddedPalette);
    }
    public static Texture2D? Decode(byte[] bytes, DatArchive? paletteArchive = null, uint palette = 0, bool clipMap = false, IReadOnlyDictionary<int,uint>? paletteColors = null,bool preferEmbeddedPalette=false)
    {
        using var image = DecodeImage(bytes, paletteArchive, palette, clipMap, paletteColors, preferEmbeddedPalette);
        if (image is null) return null;
        // Decode DXT before uploading on Android, where S3TC support varies by GPU.
        if (OS.HasFeature("android") && image.IsCompressed() && image.Decompress() != Error.Ok) return null;
        return ImageTexture.CreateFromImage(image);
    }

    internal static Image? DecodeImage(byte[] bytes, DatArchive? paletteArchive = null, uint palette = 0, bool clipMap = false,
        IReadOnlyDictionary<int, uint>? paletteColors = null, bool preferEmbeddedPalette = false)
    {
        if (bytes.Length < 24) return null;
        int Int(int p) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p));
        int w = Int(8), h = Int(12), format = Int(16), length = Int(20);
        if (w < 1 || h < 1 || w > 1024 || h > 1024 || length < 0 || length > bytes.Length - 24) return null;
        if (format == 500)
        {
            var source=bytes.AsSpan(24,length).ToArray();
            var jpg = new Image(); return jpg.LoadJpgFromBuffer(source) == Error.Ok ? Finish(jpg, clipMap) : null;
        }
        if (format is 827611204 or 861165636 or 894720068)
        {
            var type = format == 827611204 ? Image.Format.Dxt1 : format == 861165636 ? Image.Format.Dxt3 : Image.Format.Dxt5;
            int expected = ((w + 3) / 4) * ((h + 3) / 4) * (format == 827611204 ? 8 : 16);
            if (length != expected) return null;
            return Finish(Image.CreateFromData(w, h, false, type, bytes.AsSpan(24,length).ToArray()), clipMap);
        }
        if (format is not (20 or 21 or 22 or 23 or 26 or 28 or 41 or 101 or 243 or 244)) return null;
        int stride = format is 20 or 243 ? 3 : format is 23 or 26 or 101 ? 2 : format is 28 or 41 or 244 ? 1 : 4;
        if (length < w * h * stride) return null;
        uint[]? colors = null;
        if (format is 41 or 101)
        {
            var palDat = paletteArchive;
            if(bytes.Length>=28+length)
            {
                uint embedded=unchecked((uint)Int(24+length));
                // Surface/ObjDesc IDs are fallbacks in native appearance paths.
                // The optional policy preserves the explicit palette argument's
                // existing override semantics for direct callers and dye tools.
                if(palette==0||(preferEmbeddedPalette&&embedded!=0&&palDat?.Entries.ContainsKey(embedded)==true))palette=embedded;
            }
            if (palDat is null||!palDat.Entries.ContainsKey(palette)) return null;
            using var r = AcFormats.Reader(palDat.Read(palette)); r.ReadUInt32(); int count = AcFormats.Count(r, 65536);
            colors = new uint[count]; for (int i = 0; i < count; i++) colors[i] = r.ReadUInt32();
            // This palette belongs to this decode. Apply remaps once instead
            // of hashing the same palette index again for every image pixel.
            if(paletteColors is not null)foreach(var replacement in paletteColors)
                if((uint)replacement.Key<(uint)colors.Length)colors[replacement.Key]=replacement.Value;
        }
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int a = 24 + i * stride, b = i * 4;
            uint c;
            if (colors is not null)
            {
                int index = stride == 1 ? bytes[a] : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(a));
                if (index >= colors.Length) return null;
                c = colors[index];
                // AC INDEX16 reserves eight lighting variants of the key color.
                // Its RGB can be brown, green, or anything else; it is not a black key.
                if(clipMap&&index<8)c=0;
            }
            else if (format is 28 or 244) c = (uint)bytes[a]<<24 | 0x00ffffffu;
            else if (format is 23 or 26)
            {
                int p = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(a));
                c = format == 23 ? 0xff000000u | (uint)(((p >> 11) * 255 / 31) << 16 | ((p >> 5 & 63) * 255 / 63) << 8 | (p & 31) * 255 / 31)
                    : (uint)(((p >> 12) * 17) << 24 | ((p >> 8 & 15) * 17) << 16 | ((p >> 4 & 15) * 17) << 8 | (p & 15) * 17);
            }
            else if (format == 243) c = 0xff000000u | (uint)(bytes[a] << 16 | bytes[a + 1] << 8 | bytes[a + 2]);
            else c = (format == 21 ? (uint)bytes[a + 3] << 24 : 0xff000000u) | (uint)(bytes[a + 2] << 16 | bytes[a + 1] << 8 | bytes[a]);
            rgba[b] = (byte)(c >> 16); rgba[b + 1] = (byte)(c >> 8); rgba[b + 2] = (byte)c; rgba[b + 3] = (byte)(c >> 24);
        }
        // Indexed clip keys were handled above. Do not also discard legitimate
        // black palette entries elsewhere in the image.
        return Finish(Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba), clipMap&&colors is null);
    }
    private static Image Finish(Image image, bool clipMap)
    {
        if (clipMap)
        {
            // AC clip-map surfaces key out black, including old DXT1 opaque blocks.
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgba8);
            // Preserve a real alpha channel, including translucent black pixels.
            // Black-key fallback is only for legacy opaque true-color clipmaps.
            if(image.DetectAlpha()==Image.AlphaMode.None)
            {
                byte[] pixels = image.GetData();
                for (int i = 0; i < pixels.Length; i += 4)
                    if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0) pixels[i + 3] = 0;
                image = Image.CreateFromData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, pixels);
            }
        }
        return image;
    }
}
