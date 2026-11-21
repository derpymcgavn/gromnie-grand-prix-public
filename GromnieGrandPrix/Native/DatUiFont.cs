using Godot;

namespace DerethSurvivors;

internal static class DatUiFont
{
    private const int HeaderSize = 16, GlyphSize = 11, FooterSize = 20;

    internal static FontFile? Load(DatArchive dat, uint id, int nominalSize, Font fallback)
    {
        if (!dat.Entries.TryGetValue(id, out var entry) || entry.Size < HeaderSize + FooterSize) return null;
        using var reader = AcFormats.Reader(dat.Read(id));
        if (reader.ReadUInt32() != id) return null;
        uint lineHeight = reader.ReadUInt32();
        reader.ReadUInt32(); // Maximum glyph width.
        uint count = reader.ReadUInt32();
        if (lineHeight is 0 or > 128 || count == 0 || count > (entry.Size - HeaderSize - FooterSize) / GlyphSize)
            return null;

        reader.BaseStream.Position = HeaderSize + count * GlyphSize + 8L; // Skip atlas padding.
        int baseline = reader.ReadInt32();
        uint foreground = reader.ReadUInt32();
        if (baseline <= 0 || baseline > lineHeight || !dat.Entries.ContainsKey(foreground)) return null;
        using var atlas = DatTextures.DecodeImage(dat.Read(foreground), dat);
        if (atlas is null || atlas.IsCompressed() && atlas.Decompress() != Error.Ok) return null;
        atlas.Convert(Image.Format.La8);
        int atlasWidth = atlas.GetWidth(), atlasHeight = atlas.GetHeight();

        var font = new FontFile
        {
            ResourceName = $"Dereth {id:X8}", FixedSize = nominalSize, FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled,
            Antialiasing = TextServer.FontAntialiasing.None, Hinting = TextServer.Hinting.None,
            Fallbacks = [fallback]
        };
        var strike = new Vector2I(nominalSize, 0);
        font.SetCacheAscent(0, nominalSize, baseline);
        font.SetCacheDescent(0, nominalSize, lineHeight - baseline);
        font.SetTextureImage(0, strike, 0, atlas);

        reader.BaseStream.Position = HeaderSize;
        for (int i = 0; i < count; i++)
        {
            int glyph = reader.ReadUInt16(), x = reader.ReadUInt16(), y = reader.ReadUInt16();
            int w = reader.ReadByte(), h = reader.ReadByte();
            int before = reader.ReadSByte(), after = reader.ReadSByte(), top = reader.ReadSByte();
            if (glyph == 0 || x + w > atlasWidth || y + h > atlasHeight) continue;
            font.SetGlyphAdvance(0, nominalSize, glyph, new(w + before + after, 0));
            font.SetGlyphOffset(0, strike, glyph, new(before, top - baseline));
            font.SetGlyphSize(0, strike, glyph, new(w, h));
            font.SetGlyphUVRect(0, strike, glyph, new(x, y, w, h));
            font.SetGlyphTextureIdx(0, strike, glyph, 0);
        }
        int[] glyphs = font.GetGlyphList(0, strike);
        if (glyphs.Contains('A') && glyphs.Contains('0') && glyphs.Contains(' ')) return font;
        font.Dispose();
        return null;
    }
}
