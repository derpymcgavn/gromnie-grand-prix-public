using System.Collections.ObjectModel;
using System.Text.Json;

namespace DerethSurvivors;

// Original server /telepoi names and portal Destination properties. Indoor
// cells remain indoor identities; their high bits never imply an outdoor map.
public sealed class PointOfInterestCatalog
{
    public sealed class Destination
    {
        public int Id{get;init;}
        public string Name{get;init;}="";
        public int PortalWeenieId{get;init;}
        public uint Cell{get;init;}
        public string CellHex{get;init;}="";
        public uint Landblock{get;init;}
        public bool Outdoor{get;init;}
        public float X{get;init;}public float Y{get;init;}public float Z{get;init;}
        public float QW{get;init;}public float QX{get;init;}public float QY{get;init;}public float QZ{get;init;}
        public string SourceModified{get;init;}="";
    }
    private sealed class Document
    {
        public string Source{get;set;}="";
        public string Archive{get;set;}="";
        public string ArchiveSha256{get;set;}="";
        public string SqlEntry{get;set;}="";
        public string CommandSource{get;set;}="";
        public string CommandSemantics{get;set;}="";
        public Destination[] Destinations{get;set;}=Array.Empty<Destination>();
    }
    public string Provenance{get;}
    public string Archive{get;}
    public string ArchiveSha256{get;}
    public string SqlEntry{get;}
    public string CommandSource{get;}
    public string CommandSemantics{get;}
    public IReadOnlyList<Destination> Destinations{get;}
    public IReadOnlyDictionary<int,Destination> ById{get;}
    public IReadOnlyList<Destination> OutdoorDestinations{get;}
    public IReadOnlyList<Destination> IndoorDestinations{get;}
    // Groups retain every native alias, with the first source row as the
    // canonical label; no extra maps are invented for equivalent portal names.
    public IReadOnlyDictionary<int,IReadOnlyList<Destination>> InteriorGroups{get;}

    public PointOfInterestCatalog(string json)
    {
        var data=JsonSerializer.Deserialize<Document>(json)??throw new InvalidDataException("Missing native /telepoi catalog.");
        if(!Text(data.Source,512)||!Text(data.Archive,512)||!Text(data.SqlEntry,256)||!Text(data.CommandSource,512)||!Text(data.CommandSemantics,1024)
            ||data.ArchiveSha256 is null||data.ArchiveSha256.Length!=64||data.ArchiveSha256.Any(character=>!Uri.IsHexDigit(character))
            ||data.Destinations is null||data.Destinations.Length is <1 or >4096)
            throw new InvalidDataException("Invalid /telepoi source provenance.");
        var ids=new Dictionary<int,Destination>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in data.Destinations)
        {
            if(!Valid(row)||!ids.TryAdd(row.Id,row)||!names.Add(row.Name))throw new InvalidDataException("Invalid or duplicate native /telepoi destination.");
        }
        Provenance=data.Source;Archive=data.Archive;ArchiveSha256=data.ArchiveSha256;SqlEntry=data.SqlEntry;
        CommandSource=data.CommandSource;CommandSemantics=data.CommandSemantics;
        Destinations=Array.AsReadOnly(data.Destinations);ById=new ReadOnlyDictionary<int,Destination>(ids);
        OutdoorDestinations=Array.AsReadOnly(data.Destinations.Where(row=>row.Outdoor).ToArray());
        IndoorDestinations=Array.AsReadOnly(data.Destinations.Where(row=>!row.Outdoor).ToArray());
        InteriorGroups=new ReadOnlyDictionary<int,IReadOnlyList<Destination>>(IndoorDestinations.GroupBy(row=>row.PortalWeenieId)
            .ToDictionary(group=>group.Key,group=>(IReadOnlyList<Destination>)Array.AsReadOnly(group.ToArray())));
    }
    public Destination? Find(int id)=>ById.GetValueOrDefault(id);
    private static bool Text(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=maximum&&value==value.Trim()
        &&!value.Any(char.IsControl);
    private static bool Coordinate(float value)=>float.IsFinite(value)&&MathF.Abs(value)<=1_000_000;
    private static bool Valid(Destination? row)
    {
        if(row is null||row.Id<=0||row.PortalWeenieId<=0||!Text(row.Name,96)||row.Cell==0||row.Landblock!=((row.Cell&0xffff0000u)|0xffffu)
            ||!Coordinate(row.X)||!Coordinate(row.Y)||!Coordinate(row.Z)||!float.IsFinite(row.QW)||!float.IsFinite(row.QX)||!float.IsFinite(row.QY)||!float.IsFinite(row.QZ)
            ||!Text(row.SourceModified,64)||!Text(row.CellHex,8)||row.CellHex!=row.Cell.ToString("X8"))return false;
        double norm=(double)row.QW*row.QW+(double)row.QX*row.QX+(double)row.QY*row.QY+(double)row.QZ*row.QZ;
        if(norm is <.98 or >1.02)return false;
        uint cell=row.Cell&0xffffu;
        return row.Outdoor?cell is >=1 and <=64&&row.X is >=0 and <=192&&row.Y is >=0 and <=192:cell is >=0x100 and <0xffff;
    }
}
