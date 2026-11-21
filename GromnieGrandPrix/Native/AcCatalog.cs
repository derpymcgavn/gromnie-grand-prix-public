namespace DerethSurvivors;

// Asset IDs from AC's world data. Assets themselves stay in the user's DATs.
public static class AcCatalog
{
    public record Creature(string Name, uint Setup, uint Motion, uint Effects, float Scale,uint Sound=0,bool Lord=false,bool Boss=false);
    public record Projectile(uint Setup, uint Effects, float Scale);
    public static readonly Creature Player = new("Human mage", 0x02000001, 0x09000001, 0x34000004, 1);
    private static readonly Creature[] BaseEnemies = {
        new("Drudge Skulker", 0x020007dd, 0x09000008, 0x3400001a, 0.95f),
        new("Male Tusker", 0x02000964, 0x0900000c, 0x34000027, 1.1f),
        new("Olthoi Worker", 0x02000aac, 0x09000002, 0x34000021, 1),
        new("Lich", 0x02000197, 0x09000017, 0x34000028, 1.1f),
        new("Olthoi Queen", 0x02000aad, 0x090000bf, 0x34000093, 1.5f),
        new("Lady Aerfalle", 0x02001123, 0x09000001, 0x34000028, 1.1f),
        new("Jibrit Zefir",0x0200049a,0x09000069,0x3400002f,.9f,0x2000003f),
        new("K'nath Z'bog",0x020004af,0x09000032,0x3400001d,1,0x20000048),
        new("Panumbris Shadow",0x0200071b,0x09000093,0x34000063,1,0x20000002),
        new("Blood Shreth",0x020005a7,0x09000080,0x34000065,1,0x2000004a),
        new("Mite Scamp",0x02001080,0x0900000b,0x3400001f,1,0x2000000b)
    };
    public const int BaseEnemyCount=11;
    public static Creature[] Enemies{get;private set;}=BaseEnemies;
    private static Creature[] rareCreatures=Array.Empty<Creature>(),encounterCreatures=Array.Empty<Creature>(),bestiaryCreatures=Array.Empty<Creature>();
    public static void SetRareCreatures(IEnumerable<Creature> rare){rareCreatures=rare.ToArray();Rebuild();}
    public static void SetEncounterCreatures(IEnumerable<Creature> creatures){encounterCreatures=creatures.ToArray();Rebuild();}
    public static void SetBestiaryCreatures(IEnumerable<Creature> creatures){bestiaryCreatures=creatures.ToArray();Rebuild();}
    private static void Rebuild()=>Enemies=BaseEnemies.Concat(rareCreatures).Concat(encounterCreatures).Concat(bestiaryCreatures).ToArray();
    public static bool IsBoss(int archetype)=>archetype is 3 or 4 or 5||Enemies[archetype].Lord||Enemies[archetype].Boss;
    public static readonly int[] OrdinaryEnemies={0,1,2,6,7,8,9,10};
    public static readonly Dictionary<string, Projectile> Projectiles = new() {
        ["fire"] = new(0x0200040d, 0x34000005, 1),
        ["frost"] = new(0x020003f4, 0x34000006, 0.4f),
        ["acid"] = new(0x020003f6, 0x3400000b, 0.5f),
        ["lightning"] = new(0x020003f0, 0x34000007, 1),
        ["shock"] = new(0x020003fa, 0x34000009, 1),
        ["fire-ring"] = new(0x02000881, 0, 1),
        ["frost-ring"] = new(0x02000883, 0, 0.4f),
        ["acid-ring"] = new(0x02000882, 0, 0.5f),
        ["lightning-ring"] = new(0x02000884, 0, 1),
        ["lightning-wall"] = new(0x02000880, 0x34000081, 1),
        ["shock-ring"] = new(0x020015ed, 0, 1),
        ["fists"] = new(0x02000eae, 0, 1)
    };
}
