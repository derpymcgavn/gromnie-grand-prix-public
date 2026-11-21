using System.Text.Json;
namespace DerethSurvivors;
public sealed class CombatCatalog
{
    public sealed class Weapon
    {
        public int Id{get;set;}public string Kind{get;set;}="";public string Name{get;set;}="";public string Skill{get;set;}="";
        public uint Setup{get;set;}public uint Icon{get;set;}public uint AmmoSetup{get;set;}public uint Holding{get;set;}
        public bool TwoHanded{get;set;}public float Damage{get;set;}public float Range{get;set;}public float Stamina{get;set;}
        public float Scale{get;set;}=1;public uint Clothing{get;set;}public uint Palette{get;set;}public uint PaletteTemplate{get;set;}public float Shade{get;set;}=.5f;
        public int NativeSkill{get;set;}public int NativeWeaponSkill{get;set;}public uint NativeCombatStyle{get;set;}public uint NativeWeaponType{get;set;}
        public float NativeDamage{get;set;}public float NativeVariance{get;set;}public float NativeSpeed{get;set;}public uint NativeDamageType{get;set;}
        public uint NativeAmmunitionType{get;set;}public uint NativeItemType{get;set;}public float Armor{get;set;}
        public uint NativeAmmoDamageType{get;set;}public float NativeAmmoDamage{get;set;}public float NativeAmmoVariance{get;set;}public float NativeDamageMod{get;set;}=1;
        public uint EffectiveNativeDamageType=>NativeDamageType!=0?NativeDamageType:NativeAmmoDamageType;
    }
    public sealed class Summon
    {
        public int Essence{get;set;}public int Id{get;set;}public string Kind{get;set;}="";public string Name{get;set;}="";
        public uint Setup{get;set;}public uint Motion{get;set;}public uint Effects{get;set;}public uint Sound{get;set;}public uint Icon{get;set;}public float Scale{get;set;}
    }
    private sealed class Document{public Weapon[] Weapons{get;set;}=Array.Empty<Weapon>();public Summon[] Summons{get;set;}=Array.Empty<Summon>();}
    private sealed class VariantDocument{public Weapon[] Weapons{get;set;}=Array.Empty<Weapon>();public Weapon[] Variants{get;set;}=Array.Empty<Weapon>();}
    public Dictionary<string,Weapon> Weapons{get;}public Dictionary<int,Weapon> Variants{get;}public Dictionary<string,Summon> Summons{get;}
    public HashSet<int> RewardOnlyWeaponIds{get;}=new();
    public IEnumerable<Weapon> ForKind(string kind)=>Variants.Values.Where(w=>w.Kind==kind&&!RewardOnlyWeaponIds.Contains(w.Id));
    public void AddRewardWeapons(IEnumerable<Weapon> weapons)
    {
        foreach(var weapon in weapons)
        {
            if(weapon.Id<=0||!Weapons.ContainsKey(weapon.Kind)||weapon.Setup>>24!=2||weapon.Icon>>24!=6||weapon.AmmoSetup>>24!=2
                ||!float.IsFinite(weapon.Scale)||weapon.Scale<=0||weapon.Damage<=0||weapon.Range<=0)throw new InvalidDataException("Invalid native Creature Lord weapon.");
            Variants[weapon.Id]=weapon;RewardOnlyWeaponIds.Add(weapon.Id);
        }
    }
    public CombatCatalog(string json,string? variantJson=null)
    {
        var data=JsonSerializer.Deserialize<Document>(json)??throw new InvalidDataException("Missing combat catalog.");
        Weapons=data.Weapons.ToDictionary(w=>w.Kind);Summons=data.Summons.ToDictionary(s=>s.Kind);
        if(Weapons.Count!=8||Summons.Count!=3||Weapons.Values.Any(w=>w.Setup>>24!=2||w.AmmoSetup>>24!=2||w.Damage<=0||w.Stamina<=0)||Summons.Values.Any(s=>s.Setup>>24!=2||s.Motion>>24!=9||s.Scale<=0))throw new InvalidDataException("Incomplete combat assets.");
        Variants=data.Weapons.ToDictionary(w=>w.Id);
        if(variantJson is not null)
        {
            var variants=JsonSerializer.Deserialize<VariantDocument>(variantJson)??throw new InvalidDataException("Missing weapon variants.");
            foreach(var w in variants.Weapons.Concat(variants.Variants))
            {
                if(w.Id<=0||w.Setup>>24!=2||w.Icon>>24!=6||w.Scale<=0||w.Damage<=0||w.Range<=0||w.Kind!="caster"&&(!Weapons.ContainsKey(w.Kind)||w.AmmoSetup>>24!=2||w.Stamina<=0))throw new InvalidDataException($"Invalid weapon variant {w.Id}.");
                Variants[w.Id]=w;
            }
            var caster=Variants.Values.FirstOrDefault(w=>w.Kind=="caster");if(caster is not null)Weapons["caster"]=caster;
        }
    }
}
