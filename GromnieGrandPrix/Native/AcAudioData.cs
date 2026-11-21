using System.Buffers.Binary;

namespace DerethSurvivors;

// Retail DAT sound table and wave record layouts.
internal static class AcAudioData
{
    public readonly record struct Cue(uint Wave, float Probability, float Volume);
    public readonly record struct Wave(int Encoding, int Channels, int Rate, int Bits, byte[] Data);

    public static Dictionary<uint, Cue[]> ReadTable(byte[] bytes)
    {
        using var reader = AcFormats.Reader(bytes);
        reader.ReadUInt32();
        reader.ReadUInt32();
        Cue Entry()
        {
            uint wave = reader.ReadUInt32();
            reader.ReadSingle(); // Playback priority in the original client mixer.
            return new(wave, reader.ReadSingle(), reader.ReadSingle());
        }
        int hash = AcFormats.Count(reader, 10000);
        for (int i = 0; i < hash; i++) Entry();
        int count = reader.ReadUInt16();
        reader.ReadUInt16();
        var result = new Dictionary<uint, Cue[]>();
        for (int i = 0; i < count; i++)
        {
            uint cue = reader.ReadUInt32();
            var variants = new Cue[AcFormats.Count(reader, 1024)];
            for (int j = 0; j < variants.Length; j++) variants[j] = Entry();
            reader.ReadUInt32();
            result[cue] = variants;
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Native sound table layout mismatch.");
        return result;
    }

    public static Wave ReadWave(byte[] bytes)
    {
        using var reader = AcFormats.Reader(bytes);
        reader.ReadUInt32();
        int headerLength = reader.ReadInt32(), length = reader.ReadInt32();
        if (headerLength is < 16 or > 65536 || length is <= 0 or > 8 * 1024 * 1024 || 12L + headerLength + length > bytes.Length)
            throw new InvalidDataException("Invalid DAT wave lengths.");
        var header = reader.ReadBytes(headerLength);
        int encoding = BinaryPrimitives.ReadUInt16LittleEndian(header), channels = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        int rate = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)), bits = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
        if (channels is < 1 or > 2 || rate is < 1000 or > 96000 || encoding is not (1 or 85) || encoding == 1 && bits is not (8 or 16))
            throw new NotSupportedException($"DAT wave codec {encoding}/{bits} at {rate} Hz is unsupported.");
        var data = reader.ReadBytes(length);
        if (encoding == 1 && bits == 8)
            for (int i = 0; i < data.Length; i++) data[i] ^= 0x80;
        return new(encoding, channels, rate, bits, data);
    }
}
