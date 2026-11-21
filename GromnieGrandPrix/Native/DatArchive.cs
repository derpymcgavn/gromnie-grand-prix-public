using System.Buffers.Binary;
using System.Text;

namespace DerethSurvivors;

// Read-only AC archive reader. Format references are documented in README.txt.
// Does not require ACE, an AC process, a database, or a network connection.
public sealed class DatArchive : IDisposable
{
    public record Entry(uint Id, uint Offset, int Size);
    public record Spell(uint Id, string Name, string Description, uint School, uint Icon, uint Power, uint Mana, uint CasterEffect, uint TargetEffect, uint Flags);
    internal static ReadOnlySpan<byte> CompactMagic => "GGPDAT01"u8;
    internal const int CompactHeaderBytes = 16, CompactEntryBytes = 12;
    private const int MaxRecordBytes = 64 * 1024 * 1024;
    private readonly FileStream file;
    private readonly int blockSize;
    private readonly bool compact;
    public Dictionary<uint, Entry> Entries { get; } = new();
    public uint DataSet { get; }

    public DatArchive(string path)
    {
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            Span<byte> magic = stackalloc byte[8];
            file.ReadExactly(magic);
            compact = magic.SequenceEqual(CompactMagic);
            if (compact)
            {
                using var index = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
                DataSet = index.ReadUInt32();
                int count = index.ReadInt32();
                long dataStart = CompactHeaderBytes + (long)count * CompactEntryBytes;
                if (count is <= 0 or > 2_000_000 || dataStart > file.Length)
                    throw new InvalidDataException("Invalid compact DAT index length.");
                long fileLength = file.Length;
                for (int i = 0; i < count; i++)
                {
                    uint id = index.ReadUInt32(), offset = index.ReadUInt32();
                    int size = index.ReadInt32();
                    // A zero offset preserves the presence of an unreadable record from the source DAT.
                    if (size < 0 || (offset != 0 && (size > MaxRecordBytes || offset < dataStart || offset + (long)size > fileLength))
                        || !Entries.TryAdd(id, new Entry(id, offset, size)))
                        throw new InvalidDataException($"Invalid compact DAT index entry {id:X8}.");
                }
                return;
            }
            byte[] header = new byte[36];
            file.Position = 0x140;
            file.ReadExactly(header);
            uint U(int p) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(p, 4));
            blockSize = checked((int)U(4));
            DataSet = U(12);
            if (blockSize < 256 || blockSize > 65536 || (blockSize & (blockSize - 1)) != 0)
                throw new InvalidDataException("Unsupported DAT sector size.");
            var pending = new Stack<uint>();
            var visited = new HashSet<uint>();
            pending.Push(U(32));
            while (pending.TryPop(out uint sector))
            {
                if (!visited.Add(sector) || visited.Count > 100000)
                    throw new InvalidDataException("Cyclic or excessive DAT directory tree.");
                byte[] page = ReadChain(sector, 1716);
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(248, 4));
                if (count > 61) throw new InvalidDataException("Invalid DAT directory length.");
                if (BinaryPrimitives.ReadUInt32LittleEndian(page) != 0)
                    for (int i = 0; i <= count; i++)
                        pending.Push(BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(i * 4, 4)));
                for (int i = 0; i < count; i++)
                {
                    int p = 252 + i * 24;
                    uint id = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(p + 4));
                    uint offset = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(p + 8));
                    int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(p + 12)));
                    Entries[id] = new Entry(id, offset, size);
                }
            }
        }
        catch { file.Dispose(); throw; }
    }
    private byte[] ReadChain(uint offset, int size)
    {
        if (size < 0 || size > MaxRecordBytes) throw new InvalidDataException("DAT record too large.");
        var result = new byte[size];
        var seen = new HashSet<uint>();
        int written = 0;
        Span<byte> link = stackalloc byte[4];
        while (written < size)
        {
            if (offset < 0x200 || offset % blockSize != 0 || !seen.Add(offset) || offset + 4L > file.Length)
                throw new InvalidDataException("Invalid DAT sector chain.");
            file.Position = offset;
            file.ReadExactly(link);
            int take = Math.Min(blockSize - 4, size - written);
            file.ReadExactly(result.AsSpan(written, take));
            written += take;
            offset = BinaryPrimitives.ReadUInt32LittleEndian(link);
            if (written < size && offset == 0) throw new InvalidDataException("Truncated DAT record.");
        }
        return result;
    }

    public byte[] Read(uint id)
    {
        if (!Entries.TryGetValue(id, out var entry)) throw new KeyNotFoundException($"DAT record {id:X8} not found.");
        if (!compact) return ReadChain(entry.Offset, entry.Size);
        if (entry.Offset == 0) throw new InvalidDataException($"Source DAT record {id:X8} is unreadable.");
        var result = new byte[entry.Size];
        file.Position = entry.Offset;
        file.ReadExactly(result);
        return result;
    }

    public List<Spell> ReadSpells()
    {
        using var stream = new MemoryStream(Read(0x0E00000E));
        using var r = new BinaryReader(stream);
        if (r.ReadUInt32() != 0x0E00000E) throw new InvalidDataException("Spell table ID mismatch.");
        int count = r.ReadUInt16();
        r.ReadUInt16();
        var spells = new List<Spell>(count);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string Text()
        {
            int length = r.ReadUInt16();
            byte[] bytes = r.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            for (int n = 0; n < bytes.Length; n++) bytes[n] = (byte)((bytes[n] << 4) | (bytes[n] >> 4));
            stream.Position = (stream.Position + 3) & ~3L;
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
        for (int i = 0; i < count; i++)
        {
            uint id = r.ReadUInt32();
            string name = Text(), description = Text();
            uint school = r.ReadUInt32(), icon = r.ReadUInt32();
            r.ReadUInt32(); uint flags = r.ReadUInt32();
            uint mana = r.ReadUInt32();
            r.ReadSingle(); r.ReadSingle();
            uint power = r.ReadUInt32();
            r.ReadSingle(); r.ReadUInt32(); r.ReadSingle();
            uint type = r.ReadUInt32();
            r.ReadUInt32();
            if (type is 1 or 12) stream.Position += 16;
            else if (type == 7) stream.Position += 8;
            // Eight formula components, three effects, recovery data and UI fields.
            stream.Position += 32;
            uint casterEffect = r.ReadUInt32(), targetEffect = r.ReadUInt32();
            stream.Position += 4 + 8 + 4 + 12;
            if (stream.Position > stream.Length) throw new InvalidDataException($"Truncated spell {id}.");
            spells.Add(new(id, name, description, school, icon, power, mana, casterEffect, targetEffect, flags));
        }
        return spells;
    }
    public void Dispose() => file.Dispose();
}
