using System.Security.Cryptography;
using System.Text.Json;
using GodotFile = Godot.FileAccess;

internal static class AndroidDatInstall
{
    private const string Bundle = "res://BundledDats";
    private const int PartBytes = 16 * 1024 * 1024;
    private const int ReadBytes = 1024 * 1024;
    private static readonly string[] Required = ["client_portal.dat", "client_cell_1.dat"];
    private sealed record Archive(long Bytes, string Sha256);

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
