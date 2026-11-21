using System.Text.Json;

namespace DerethSurvivors;

public sealed class ArmorCatalog
{
    public sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public uint Clothing { get; set; }
        public uint Icon { get; set; }
        public uint Palette { get; set; }
        public float Shade { get; set; }
        public uint Priority { get; set; }
        public uint Locations { get; set; }
        public uint Setup { get; set; }
        public float Armor { get; set; }
        public string Family => Name.Contains("Shadow") ? "Shadow" : Name.StartsWith("Studded Leather") ? "Studded Leather" : Name.Split(' ')[0];
        public bool CraftOnly => Family == "Shadow";
        public int MinTier => Family switch { "Leather" or "Chainmail" => 1, "Studded Leather" or "Scalemail" or "Yoroi" => 2, "Amuli" or "Koujia" or "Chiran" or "Platemail" => 3, "Celdon" => 4, "Covenant" => 5, _ => 6 };
        public string[] Slots => Coverage.Where(p => (Locations & p.Mask) != 0).Select(p => p.Slot).ToArray();
        public string Slot => Slots.FirstOrDefault() ?? "Chest";
    }
    private sealed class Document { public Item[] Items { get; set; } = Array.Empty<Item>(); }
    public static readonly (string Slot, uint Mask, float Weight)[] Coverage = {
        ("Head",1,0.07f),("Chest",512,0.25f),("Abdomen",1024,0.18f),("Upper arms",2048,0.1f),
        ("Lower arms",4096,0.08f),("Hands",32,0.04f),("Upper legs",8192,0.12f),("Lower legs",16384,0.1f),("Feet",256,0.06f)
    };
    public Dictionary<int, Item> Items { get; }
    public HashSet<int> RewardOnlyArmorIds{get;}=new();
    public void AddRewardArmor(IEnumerable<Item> items)
    {foreach(var item in items){if(item.Id<=0||item.Clothing>>24!=0x10||item.Icon>>24!=6||item.Slots.Length==0)throw new InvalidDataException("Invalid Creature Lord armor.");Items[item.Id]=item;RewardOnlyArmorIds.Add(item.Id);}}
    public ArmorCatalog(string json) => Items = (JsonSerializer.Deserialize<Document>(json)?.Items ?? throw new InvalidDataException("Missing armor item catalog.")).ToDictionary(i => i.Id);
    public static readonly int[] Starter = {45,39,60,86,36,56,109,65,115};
}
