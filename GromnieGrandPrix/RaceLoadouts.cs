using Godot;
using DerethSurvivors;

internal enum SideShotKind { None, Javelin, Lightning }
internal enum ShieldImpact { Swipe, Spell, Collision }

internal sealed record RaceWeapon(string Name, uint Setup, uint Icon, float Reach, float Cooldown, int BalanceLoss,
    string Description, float Lengthwise = 2.45f, float Windup = .12f, float Scale = 1, SideShotKind Shot = SideShotKind.None)
{
    internal const float SwingDuration = .34f;
    internal static readonly RaceWeapon[] All =
    [
        new("Kaskara", 0x02000065, 0x06001661, 3.25f, 1.04f, 35, "A balanced slash with good side reach"),
        new("Dagger", 0x0200012F, 0x060015CB, 2.35f, .70f, 26, "Short reach and fast recovery"),
        new("Morning Star", 0x0200013C, 0x06001625, 2.65f, 1.26f, 50, "A clean hit takes half a rival’s balance"),
        new("Spear", 0x02000144, 0x0600164D, 4.5f, 1.30f, 34, "Long thrust that needs close front-to-back alignment",
            Lengthwise: 1.45f, Windup: .17f, Scale: .79f),
        new("Greataxe", 0x020018E2, 0x06006B56, 2.9f, 1.55f, 43, "A broad swing catches a rival slightly ahead or behind",
            Lengthwise: 3.65f, Windup: .21f, Scale: .75f),
        new("Javelin", 0x02000132, 0x060010C9, 10, 2.25f, 38, "Throws a straight side shot across several lanes",
            Lengthwise: 1.25f, Windup: .16f, Scale: .84f, Shot: SideShotKind.Javelin),
        new("Storm Staff", 0x0200024E, 0x0600151E, 8, 2.60f, 28, "Fires a wide lightning bolt that cuts a rival’s active boost",
            Lengthwise: 1.9f, Windup: .18f, Scale: .80f, Shot: SideShotKind.Lightning)
    ];
}

internal sealed record RaceShield(string Name, RareCatalog.Creature? Look, uint Icon,
    ShieldImpact? Specialty, float Window, float Recharge, float CounterBoost, Color Tint, string Description)
{
    internal bool Blocks(ShieldImpact impact) => Look is not null && (Specialty is null || Specialty == impact);

    internal static readonly RaceShield[] All =
    [
        new("No shield", null, 0, null, 0, 0, 0, Colors.White, "Race without a shield"),
        new("Round Shield", new RareCatalog.Creature
            { Id = 93, Setup = 0x02000162, Clothing = 268435806, Palette = 0x04000BEF, PaletteTemplate = 4, Shade = .5f, Scale = 1 },
            0x060013FF, ShieldImpact.Swipe, .45f, 2.25f, .64f, new("ffe5a2"), "Parries a close weapon swing and gives a short boost"),
        new("Tower Shield", new RareCatalog.Creature
            { Id = 95, Setup = 0x02000161, Clothing = 268435611, Palette = 0x04000BEF, PaletteTemplate = 4, Shade = .5f, Scale = .86f },
            0x06000FE2, null, .82f, 5.0f, 0, new("8ef0da"), "Blocks one attack or obstacle hit during a wider guard window"),
        new("Prismatic Diamond Shield", new RareCatalog.Creature
            { Id = 33257, Setup = 0x02000A33, Clothing = 268436256, Scale = 1.17f },
            0x06002A2C, ShieldImpact.Spell, .58f, 3.65f, .77f, new("b9a8ff"), "Absorbs one spell and turns the energy into a short boost")
    ];
}
