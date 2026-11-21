using System.Buffers.Binary;
using System.Text.RegularExpressions;
using DerethSurvivors;

if (args.Length != 3)
    throw new ArgumentException("Expected the original DAT directory, compact DAT directory and game source directory.");

string source = Path.GetFullPath(args[0]);
string destination = Path.GetFullPath(args[1]);
string project = Path.GetFullPath(args[2]);
if (StringComparer.OrdinalIgnoreCase.Equals(source, destination))
    throw new ArgumentException("The compact DATs need a separate output directory.");

Directory.CreateDirectory(destination);
const string portalName = "client_portal.dat", cellName = "client_cell_1.dat";
using (var portal = new DatArchive(Path.Combine(source, portalName)))
{
    if (portal.DataSet != 1) throw new InvalidDataException("Expected the portal DAT dataset.");
    var textures = TextureDependencies(portal, project);
    WriteArchive(portal, Path.Combine(destination, portalName), id => RetainPortal(id) || textures.Contains(id));
}
using (var cells = new DatArchive(Path.Combine(source, cellName)))
{
    if (cells.DataSet != 2) throw new InvalidDataException("Expected the cell DAT dataset.");
    // Races and the track editor read outdoor terrain and landblock object/building placements.
    WriteArchive(cells, Path.Combine(destination, cellName), id => id == 0xffff0001 || (id & 0xffff) is 0xfffe or 0xffff);
}

static bool RetainPortal(uint id) => id == 0xffff0001 || (id >> 24) switch
{
    // Setup, model, motion, animation, sound and appearance classes remain open-ended for
    // the outdoor editor and imported ACE riders, including remote rider snapshots.
    0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x08 or 0x09 or 0x0a or
    0x0f or 0x10 or 0x11 or 0x12 or 0x20 or 0x32 or 0x33 or 0x34 or 0x40 => true,
    0x0e => id is 0x0e000002 or 0x0e00000e, // Character creation and spells.
    0x13 => id == 0x13000000,              // Terrain and scenery region descriptors.
    _ => false
};

static HashSet<uint> TextureDependencies(DatArchive archive, string project)
{
    var roots = new HashSet<uint>();
    // Direct icon and texture references in game code. Native DAT-dependent references are handled below.
    var pattern = new Regex(@"\b0x(06[0-9a-f]{6})(?![0-9a-f])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    var sourceFiles = Directory.EnumerateFiles(project, "*.cs")
        .Concat(Directory.EnumerateFiles(Path.Combine(project, "Native"), "*.cs", SearchOption.AllDirectories));
    foreach (string file in sourceFiles)
        foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            AddTexture(Convert.ToUInt32(match.Groups[1].Value, 16));

    foreach (uint id in archive.Entries.Keys.Where(RetainPortal))
    {
        switch (id >> 24)
        {
            case 0x05: // Texture indirection. AcAssets chooses the first existing ID without the high-res DAT.
                ReadTextureTable(id);
                break;
            case 0x08: // Surface definitions.
            case 0x0e: // Character and spell icons.
            case 0x10: // Clothing substitutions.
            case 0x13: // Terrain albedos, details and masks.
            case 0x40: // Font atlases.
                byte[] bytes = archive.Read(id);
                // Read at byte boundaries to cover unaligned native references in these records.
                for (int i = 0; i + 4 <= bytes.Length; i++)
                    if (bytes[i + 3] == 0x06) AddTexture(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)));
                break;
        }
    }
    return roots;

    void AddTexture(uint id)
    {
        if (archive.Entries.ContainsKey(id)) roots.Add(id);
    }

    void ReadTextureTable(uint table)
    {
        byte[] data = archive.Read(table);
        if (data.Length < 13) throw new InvalidDataException($"Truncated texture table {table:X8}.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(9));
        if (count is < 0 or > 1000 || 13 + count * 4 > data.Length)
            throw new InvalidDataException($"Invalid texture table {table:X8}.");
        for (int i = 0; i < count; i++)
        {
            uint candidate = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(13 + i * 4));
            if (!archive.Entries.ContainsKey(candidate)) continue;
            roots.Add(candidate);
            break;
        }
    }
}

static void WriteArchive(DatArchive source, string output, Func<uint, bool> include)
{
    var rows = source.Entries.Values.Where(row => include(row.Id)).OrderBy(row => row.Id).ToArray();
    string partial = output + ".partial";
    int unreadable = 0;
    try
    {
        using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(DatArchive.CompactMagic);
            writer.Write(source.DataSet);
            writer.Write(rows.Length);
            file.Position += (long)rows.Length * DatArchive.CompactEntryBytes;
            var offsets = new uint[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                byte[] bytes;
                try { bytes = source.Read(rows[i].Id); }
                catch (Exception error) when (error is InvalidDataException or EndOfStreamException)
                {
                    unreadable++;
                    continue;
                }
                offsets[i] = checked((uint)file.Position);
                writer.Write(bytes);
            }
            file.Position = DatArchive.CompactHeaderBytes;
            for (int i = 0; i < rows.Length; i++)
            {
                writer.Write(rows[i].Id);
                writer.Write(offsets[i]);
                writer.Write(rows[i].Size);
            }
        }
        File.Move(partial, output, overwrite: true);
        Console.WriteLine($"{Path.GetFileName(output)}: {rows.Length:N0}/{source.Entries.Count:N0} records, " +
            $"{new FileInfo(output).Length:N0} bytes, {unreadable} unreadable in source.");
    }
    finally { File.Delete(partial); }
}
