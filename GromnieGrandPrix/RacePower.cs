internal sealed record RacePower(RaceItem Item, string Name, string Hint, uint Icon, string Tint, int Charges = 1)
{
    public static readonly RacePower[] All =
    [
        new(RaceItem.FrostVolley, "Frost Volley", "Fire three straight ice shots", 0x06002728, "8ee8ff", 3),
        new(RaceItem.PyrealSling, "Pyreal Sling", "Throw a trail of shared Pyreals", 0x0600229f, "ffc95f"),
        new(RaceItem.PyrealCache, "Pyreal Cache", "Mint a Pyreal and surge forward", 0x060016cb, "ffde88", 3),
        new(RaceItem.SmithHammer, "Smith's Hammer", "Lob a hammer into the road", 0x06001689, "e99165"),
        new(RaceItem.LeapingDraught, "Leaping Draught", "Leap over hazards and attacks", 0x060024e1, "a3f5bf"),
        new(RaceItem.ZefirHex, "Zefir Hex", "Turn rivals ahead into rabbits", 0x060013c6, "e6adff"),
        new(RaceItem.TuskerMight, "Tusker Might", "Grow huge and bowl rivals over", 0x06001371, "f7ad72"),
        new(RaceItem.Invulnerability, "Invulnerability", "Surge ahead immune to hits", 0x060013ab, "fff294"),
        new(RaceItem.ForceBurst, "Force Burst", "Blast away nearby rivals and shots", 0x060013b7, "a4f5ff"),
        new(RaceItem.WispThief, "Wisp Thief", "Phase out and steal a held item", 0x060029bf, "b8a7ff"),
        new(RaceItem.UlgrimFolly, "Ulgrim's Folly", "Splash a rival to flip their controls", GrandPrix.CastingSteinIcon, "f3b84e")
    ];

    private static readonly Dictionary<RaceItem, RacePower> ByItem = All.ToDictionary(power => power.Item);
    public static RacePower? Find(RaceItem item) => ByItem.GetValueOrDefault(item);
}
