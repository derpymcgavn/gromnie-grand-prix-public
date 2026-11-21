namespace DerethSurvivors;

internal static class AceNativeDatInfo
{
    internal const uint IterationRecord = 0xffff0001;
    private static readonly (string File, int Id, int Type, bool Required)[] Files =
    [
        ("client_portal.dat", 1, 0, true),
        ("client_cell_1.dat", 2, 0, true),
        ("client_local_English.dat", 3, 1, false),
        ("client_highres.dat", 1, 0x69466948, false)
    ];

    internal static byte[] Read(string folder, CancellationToken cancellation)
    {
        var records = new List<(int Type, int Id, byte[] Data)>();
        foreach (var (name, id, type, required) in Files)
        {
            cancellation.ThrowIfCancellationRequested();
            string path = Path.Combine(folder, name);
            if (!File.Exists(path) && !required) continue;
            using var dat = new DatArchive(path);
            if (!dat.Entries.TryGetValue(IterationRecord, out var entry) || entry.Size is < 4 or > 4096)
            {
                if (!required) continue;
                throw new InvalidDataException($"{name} is missing the DAT version record used for ACE sign-in.");
            }
            records.Add((type, id, dat.Read(IterationRecord)));
        }

        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        writer.Write(0xf7e6u); // Client DAT information.
        writer.Write(1u);
        writer.Write(records.Count);
        foreach (var (type, id, version) in records)
        {
            writer.Write(type);
            writer.Write(id);
            WriteVersion(writer, version);
        }
        writer.Write(0u);
        writer.Write(0u);
        return data.ToArray();
    }

    private static void WriteVersion(BinaryWriter output, byte[] version)
    {
        using var input = new BinaryReader(new MemoryStream(version, writable: false));
        int remaining = input.ReadInt32();
        if (remaining < 0) throw new InvalidDataException("The DAT iteration count is invalid.");
        output.Write(remaining);
        while (remaining > 0)
        {
            int length = input.ReadInt32();
            int first = input.ReadInt32();
            if (length >= 0 || remaining + length < 0)
                throw new InvalidDataException("The DAT iteration range is invalid.");
            output.Write(length);
            output.Write(first);
            remaining += length;
        }
    }
}
