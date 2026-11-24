using Godot;
using DerethSurvivors;

internal enum RaceMountKind { Gromnie, Cow, Auroch, Chicken, Rabbit, Asheron, Grievver, Mukkir, Shreth, Mattekar, Reedshark, BaelZharon, FestivusCarpet, IsinDuleDisc, CarenziSkates, Siraluun, Turkey, Tusker, SnowTusker }
internal enum MountPlatform { None, Carpet, MageDisc }

internal sealed partial record RaceMount(RaceMountKind Kind, string Name, RareCatalog.Creature? Native, float Scale,
    int SeatPart, Vector3 SeatOffset, float LegSpread, float GaitRate, MountFoot[] Feet, float SaddleFollow = 1,
    MountPlatform Platform = MountPlatform.None)
{
    // Neutral-pose offsets become joint-local sole positions when a racer is built.
    private static readonly MountFoot[] Hooves =
    [
        new(8, new(0, -.177f, -.069f)), new(11, new(0, -.177f, -.069f)),
        new(14, new(0, -.177f, -.069f)), new(17, new(0, -.177f, -.069f))
    ];

    // Mattekars and Reedsharks share a three-legged running rig.
    private static readonly MountFoot[] SharkFeet =
    [
        new(6, new(0, -.075f, -.10f)), new(9, new(0, -.075f, -.10f)), new(12, new(0, -.07f, -.05f))
    ];

    private static readonly MountFoot[] TuskerContacts =
    [
        new(4, new(0, -.30f, -.04f)), new(7, new(0, -.30f, -.04f)),
        new(10, new(0, -.175f, -.095f)), new(13, new(0, -.175f, -.095f))
    ];

    internal static readonly (string Name, string Hex, RareCatalog.Creature Look)[] SiraluunColors =
    [
        ("Marsh", "418931", SiraluunLook(11488, 4)),
        ("Tidal", "91b1c7", SiraluunLook(11490, 1)),
        ("Littoral", "4d459c", SiraluunLook(11487, 17)),
        ("Strand", "2b779c", SiraluunLook(11489, 0)),
        ("Timber", "92512f", SiraluunLook(11491, 8)),
        ("Kithless", "ba8135", SiraluunLook(11486, 39)),
        ("Untamed", "277b72", SiraluunLook(27713, 13)),
        ("Badlands", "9f3c6d", SiraluunLook(27712, 14))
    ];

    public static readonly RaceMount[] All =
    [
        new(RaceMountKind.Gromnie, "Gromnie", null, 1.63f, 0, new(0, .40f, .17f), 1, 1, []),
        new(RaceMountKind.Cow, "Cow", new RareCatalog.Creature
        {
            Id = 14, Setup = 0x02000006, Motion = 0x0900000d, Palette = 0x04001db8, Clothing = 0x1000005e,
            PaletteTemplate = 39, Shade = 0, Appearance = new()
            {
                ["weenie_properties_anim_part"] = [["10", "16778431"]],
                ["weenie_properties_texture_map"] = [["7", "83889344", "83887054"]],
                ["weenie_properties_palette"] = [["67110320", "160", "8"]]
            }
        }, 1.18f, 0, new(0, .45f, .15f), .94f, 1.13f, Hooves),
        new(RaceMountKind.Auroch, "Auroch Bull", new RareCatalog.Creature
        {
            Id = 20, Setup = 0x0200002e, Motion = 0x09000019, Palette = 0x040001b6, Clothing = 0x1000005c,
            PaletteTemplate = 43, Appearance = new()
            {
                ["weenie_properties_anim_part"] = [["16", "16795640"]],
                ["weenie_properties_texture_map"] = [["16", "83886837", "83890287"]],
                ["weenie_properties_palette"] = [["67109558", "0", "24"]]
            }
        }, 1.24f, 0, new(0, .48f, .28f), 1.04f, 1.10f, Hooves),
        new(RaceMountKind.Chicken, "Chicken", new RareCatalog.Creature
        {
            Id = 24937, Setup = 0x020005a2, Motion = 0x0900012c, Palette = 0x040015cf, Clothing = 0x100004b4,
            PaletteTemplate = 17
        }, 3.42f, 0, new(0, .29f, .015f), 1.10f, 1.19f,
            [new(2, new(0, -.198f, -.052f)), new(4, new(0, -.198f, -.052f))]),
        new(RaceMountKind.Rabbit, "Bunny", new RareCatalog.Creature
        {
            Id = 2568, Setup = 0x0200047b, Motion = 0x09000062, Palette = 0x040001b4, Clothing = 0x1000010d,
            PaletteTemplate = 61
        }, 4.65f, 0, new(0, .117f, .018f), .81f, 1.04f,
            [new(5, new(0, -.097f, -.035f)), new(7, new(0, -.097f, -.035f)),
             new(8, new(0, -.078f, -.036f)), new(9, new(0, -.078f, -.036f))]),
        new(RaceMountKind.Asheron, "Asheron (Piggyback)", new RareCatalog.Creature
        {
            Id = 8675, Setup = 0x020009c8, Motion = 0x09000001
        }, 1.53f, 9, new(0, -.10f, .34f), 1.04f, 1.02f,
            [new(4, Vector3.Zero), new(8, Vector3.Zero)]),
        new(RaceMountKind.Grievver, "Grievver", new RareCatalog.Creature
        {
            Id = 7981, Setup = 0x020008da, Motion = 0x0900009a, Palette = 0x04000fdf, Clothing = 0x10000246,
            PaletteTemplate = 5
        }, 1.32f, 0, new(0, .73f, .40f), .93f, 1.07f,
            [new(3, new(.06f, -1.14f, -.30f)), new(6, new(-.06f, -1.14f, -.29f)),
             new(9, new(.20f, -.85f, .37f)), new(12, new(-.19f, -.84f, .39f))]),
        new(RaceMountKind.Mukkir, "Mukkir", new RareCatalog.Creature
        {
            Id = 33130, Setup = 0x020014bd, Motion = 0x09000194, Palette = 0x04001ee3, Clothing = 0x10000645,
            PaletteTemplate = 39
        }, 1.54f, 1, new(0, .43f, -.015f), 1.07f, 1.12f,
            [new(12, new(.07f, -.02f, -.22f)), new(16, new(-.06f, -.03f, -.22f))], SaddleFollow: .32f),
        new(RaceMountKind.Shreth, "Shreth", new RareCatalog.Creature
        {
            Id = 4110, Setup = 0x020005a7, Motion = 0x09000080, Palette = 0x04000dfc, Clothing = 0x10000160,
            PaletteTemplate = 14
        }, 1.10f, 0, new(0, .32f, .16f), 1.11f, .91f,
            [new(4, new(-.01f, -.27f, -.49f)), new(7, new(-.01f, -.27f, -.49f)),
             new(10, new(.10f, -.18f, -.30f)), new(13, new(-.09f, -.18f, -.30f))]),
        new(RaceMountKind.Mattekar, "Mattekar", new RareCatalog.Creature
        {
            Id = 2582, Setup = 0x02000486, Motion = 0x09000067, Palette = 0x04000bd5, Clothing = 0x10000111,
            PaletteTemplate = 9
        }, 2.62f, 0, new(0, .26f, .09f), 1.19f, 1.05f, SharkFeet),
        new(RaceMountKind.Reedshark, "Reedshark", new RareCatalog.Creature
        {
            Id = 221, Setup = 0x02000039, Motion = 0x0900001a, Palette = 0x040001c1, Clothing = 0x10000064,
            PaletteTemplate = 62
        }, 2.40f, 0, new(0, .25f, .17f), 1.00f, .99f, SharkFeet),
        new(RaceMountKind.BaelZharon, "Bael’Zharon (Front Carry)", new RareCatalog.Creature
        {
            Id = 8503, Setup = 0x0200099e, Motion = 0x090000a4, Sound = 0x2000001e,
            Palette = 0x04001071, Clothing = 0x1000027a, PaletteTemplate = 9, Shade = 1
        }, 2.23f, 9, new(0, -.07f, -.48f), .50f, 1.02f, []),
        new(RaceMountKind.FestivusCarpet, "Festivus Flying Carpet", new RareCatalog.Creature { Id = 36909, Setup = 0x0200176b },
            1, 0, Vector3.Zero, .45f, 1, [], Platform: MountPlatform.Carpet),
        new(RaceMountKind.IsinDuleDisc, "Isin Dule’s Mage Disc", new RareCatalog.Creature { Id = 33106, Setup = 0x02001573 },
            1, 0, Vector3.Zero, .28f, 1, [], Platform: MountPlatform.MageDisc),
        new(RaceMountKind.CarenziSkates, "Carenzi Skates", CarenziRacer(38945, 14),
            1.66f, 1, Vector3.Zero, .34f, 1.04f,
            [new(7, new(0, -.028f, -.014f)), new(10, new(0, -.028f, -.014f)),
                new(15, new(0, -.028f, -.014f)), new(18, new(0, -.028f, -.014f))]),
        new(RaceMountKind.Siraluun, "Siraluun", SiraluunColors[0].Look, 1.18f, 0, new(0, .46f, .24f), .94f, 1.08f,
            [new(8, new(0, -.042f, -.17f)), new(11, new(0, -.042f, -.17f))], SaddleFollow: .54f),
        new(RaceMountKind.Turkey, "Turkey", new RareCatalog.Creature
        {
            Id = 53015, Setup = 0x02001c10, Motion = 0x090001c7, Sound = 0x200000da
        }, 3.46f, 0, new(0, .25f, -.115f), 1.10f, .95f,
            [new(2, new(0, -.198f, -.073f)), new(4, new(0, -.198f, -.073f))], SaddleFollow: .62f),
        new(RaceMountKind.Tusker, "Tusker", new RareCatalog.Creature
        {
            Id = 11, Setup = 0x02000964, Motion = 0x0900000c, Sound = 0x20000011,
            Palette = 0x0400102f, Clothing = 0x1000025b, PaletteTemplate = 76
        }, 1.32f, 1, new(0, .54f, .02f), 1.14f, 1.09f, TuskerContacts, SaddleFollow: .38f),
        new(RaceMountKind.SnowTusker, "Snow Tusker", SnowTuskerLook(43592, 0x02001a33),
            1.38f, 1, new(0, .60f, .02f), 1.16f, 1.06f, TuskerContacts, SaddleFollow: .38f)
    ];

    private static RareCatalog.Creature SiraluunLook(int id, uint paletteTemplate) => new()
    {
        Id = id, Setup = 0x02000a43, Motion = 0x090000bb, Sound = 0x2000007a,
        Palette = paletteTemplate == 0 ? 0u : 0x0400111fu,
        Clothing = paletteTemplate == 0 ? 0u : 0x100002e2u,
        PaletteTemplate = paletteTemplate
    };

    internal RareCatalog.Creature ResolveLook(RareCatalog.Creature gromnie, int color) =>
        ResolveColor(color).Look ?? Native ?? gromnie;

    internal static RareCatalog.Creature CarenziRacer(int id, uint paletteTemplate) => new()
    {
        Id = id, Setup = 0x02001019, Motion = 0x090000bd, Sound = 0x2000007b,
        Palette = 0x040016e2, Clothing = 0x100004fc, PaletteTemplate = paletteTemplate
    };

    private static RareCatalog.Creature SnowTuskerLook(int id, uint setup) => new()
    {
        Id = id, Setup = setup, Motion = 0x0900000c, Sound = 0x20000011
    };

    public static RaceMount Get(RaceMountKind kind) => All[(int)kind];
}

internal readonly record struct MountFoot(int Part, Vector3 Offset);
