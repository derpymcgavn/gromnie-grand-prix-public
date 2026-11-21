using System.Text.Json;
namespace DerethSurvivors;
public sealed class RareCatalog
{
    public class Creature
    {
        public int Id{get;set;}public string Name{get;set;}="";public bool Lord{get;set;}
        public uint Setup{get;set;}public uint Motion{get;set;}public uint Sound{get;set;}public uint Effects{get;set;}
        public uint Palette{get;set;}public uint Clothing{get;set;}public uint PaletteTemplate{get;set;}public float Shade{get;set;}=.5f;
        public float Scale{get;set;}=1;public int Level{get;set;}public uint[] Spells{get;set;}=Array.Empty<uint>();
        public float NativeTranslucency{get;set;}
        public Dictionary<string,string[][]> Appearance{get;set;}=new();
    }
    public sealed class Encounter{public int Lord{get;set;}public int[] Minions{get;set;}=Array.Empty<int>();public string Biome{get;set;}="";}
    private sealed class Document{public Creature[] Creatures{get;set;}=Array.Empty<Creature>();public Creature[] Looks{get;set;}=Array.Empty<Creature>();public Encounter[] Encounters{get;set;}=Array.Empty<Encounter>();}
    public Dictionary<int,Creature> Creatures{get;}public Dictionary<int,Creature> Looks{get;}public Encounter[] Encounters{get;}
    public RareCatalog(string json)
    {
        var data=JsonSerializer.Deserialize<Document>(json)??throw new InvalidDataException("Missing rare encounter data.");
        Creatures=data.Creatures.ToDictionary(c=>c.Id);Looks=data.Creatures.Concat(data.Looks).ToDictionary(c=>c.Id);Encounters=data.Encounters;
        if(Encounters.Length!=21||Encounters.Select(e=>e.Lord).Distinct().Count()!=21||Looks.Values.Any(c=>c.Setup>>24!=2||c.Motion>>24!=9||c.Scale<=0||!float.IsFinite(c.NativeTranslucency)||c.NativeTranslucency is <0 or >1)||Encounters.Any(e=>!Creatures.TryGetValue(e.Lord,out var lord)||!lord.Lord||e.Minions.Length==0||e.Minions.Any(id=>!Creatures.ContainsKey(id))))throw new InvalidDataException("Invalid Creature Lord roster.");
    }
}
