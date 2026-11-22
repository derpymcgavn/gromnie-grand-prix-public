using System.Security.Cryptography;
using System.Text.Json;
using DerethSurvivors;
using GodotFile = Godot.FileAccess;

internal static class AndroidDatInstall
{
    private const string Bundle = "res://BundledDats";
    private const int PartBytes = 16 * 1024 * 1024;
    private const int ReadBytes = 1024 * 1024;
    private static readonly string[] Required = ["client_portal.dat", "client_cell_1.dat"];
    private const string ImportMarker = "import.complete";
    private sealed record Archive(long Bytes, string Sha256);

    internal static bool HasBundle => GodotFile.FileExists($"{Bundle}/manifest.json");

    internal static bool HasImport(string folder) =>
        File.Exists(Path.Combine(folder, ImportMarker)) && Required.All(name => File.Exists(Path.Combine(folder, name)));

    internal static void Import(string treeUri, string folder, IProgress<(long Bytes, long Total)> progress)
    {
        if (!treeUri.StartsWith("content://", StringComparison.Ordinal))
            throw new InvalidDataException("Choose a folder in the Android file picker.");

        using var portal = Open("client_portal.dat");
        using var cells = Open("client_cell_1.dat");
        long portalBytes = Length(portal), cellBytes = Length(cells);
        long completed = 0;
        Directory.CreateDirectory(folder);
        string marker = Path.Combine(folder, ImportMarker);
        try
        {
            Copy(portal, Required[0], portalBytes);
            Copy(cells, Required[1], cellBytes);
            using (var portalDat = new DatArchive(Partial(Required[0])))
            using (var cellDat = new DatArchive(Partial(Required[1])))
            {
                if (!portalDat.Entries.ContainsKey(0x13000000) ||
                    !cellDat.Entries.Keys.Any(id => (id & 0xffff) == 0xffff))
                    throw new InvalidDataException("The selected files do not contain Asheron's Call race data.");
            }

            File.Delete(marker);
            foreach (string name in Required)
                File.Move(Partial(name), Path.Combine(folder, name), overwrite: true);
            File.WriteAllText(marker, "");
        }
        finally
        {
            foreach (string name in Required) File.Delete(Partial(name));
        }

        GodotFile Open(string name) => GodotFile.Open($"{treeUri}#{name}", GodotFile.ModeFlags.Read)
            ?? throw new FileNotFoundException($"The selected folder does not contain a readable {name}.");

        static long Length(GodotFile file) => file.GetLength() is > 0 and <= uint.MaxValue
            ? (long)file.GetLength() : throw new InvalidDataException("A selected DAT file has an invalid size.");

        string Partial(string name) => Path.Combine(folder, name + ".partial");

        void Copy(GodotFile input, string name, long remaining)
        {
            using var output = new FileStream(Partial(name), FileMode.Create, FileAccess.Write, FileShare.None);
            while (remaining > 0)
            {
                int count = (int)Math.Min(ReadBytes, remaining);
                byte[] data = input.GetBuffer(count);
                if (data.Length != count) throw new InvalidDataException($"Could not read all of {name}.");
                output.Write(data);
                completed += count;
                remaining -= count;
                if (completed % (8 * ReadBytes) == 0 || remaining == 0)
                    progress.Report((completed, portalBytes + cellBytes));
            }
            output.Flush(flushToDisk: true);
        }
    }

    internal static long Install(string folder, IProgress<(long Bytes, long Total)> progress)
    {
        var manifest = JsonSerializer.Deserialize<Dictionary<string, Archive>>(GodotFile.GetFileAsString($"{Bundle}/manifest.json"));
        if (manifest is null || Required.Any(name => !manifest.TryGetValue(name, out Archive? archive)
            || archive is null || archive.Bytes <= 0 || archive.Sha256?.Length != 64))
            throw new InvalidDataException("Invalid bundled DAT manifest.");

        Directory.CreateDirectory(folder);
        long total = Required.Sum(name => manifest[name].Bytes);
        long completed = 0, copied = 0;
        foreach (string name in Required)
        {
            Archive archive = manifest[name];
            string path = Path.Combine(folder, name);
            string marker = path + ".sha256";
            string partial = path + ".partial";
            // Remove leftovers from an interrupted install. A matching marker is written only after verification.
            File.Delete(partial);
            if (File.Exists(path) && new FileInfo(path).Length == archive.Bytes &&
                File.Exists(marker) && File.ReadAllText(marker) == archive.Sha256)
            {
                completed += archive.Bytes;
                continue;
            }

            File.Delete(marker);
            File.Delete(path);
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    long remaining = archive.Bytes;
                    for (int part = 0; remaining > 0; part++)
                    {
                        string asset = $"{Bundle}/{name}.{part:D3}.bin";
                        using var input = GodotFile.Open(asset, GodotFile.ModeFlags.Read);
                        int bytes = (int)Math.Min(PartBytes, remaining);
                        if (input is null || input.GetLength() != (ulong)bytes)
                            throw new InvalidDataException($"Missing or invalid asset {asset}.");
                        while (bytes > 0)
                        {
                            int count = Math.Min(ReadBytes, bytes);
                            var data = input.GetBuffer(count);
                            if (data.Length != count) throw new InvalidDataException($"Truncated asset {asset}.");
                            output.Write(data);
                            hash.AppendData(data);
                            bytes -= count;
                            completed += count;
                            remaining -= count;
                        }
                        progress.Report((completed, total));
                    }
                    output.Flush(flushToDisk: true);
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(archive.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Checksum mismatch for {name}.");
                File.Move(partial, path);
                File.WriteAllText(marker, archive.Sha256);
                copied += archive.Bytes;
            }
            finally
            {
                File.Delete(partial);
            }
        }
        return copied;
    }
}
