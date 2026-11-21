internal sealed partial record RaceMount
{
    private static readonly MountColor[] GromnieStock =
    [
        new("Azure", "458a9d"), new("Rust", "a8513b"), new("Ebon", "373d43"), new("Ivory", "e7d8ad"),
        new("Jade", "50a074"), new("Ash", "aea496"), new("Swamp", "a59854"), new("Amethyst", "956db5")
    ];

    // Same-setup appearances from ACE's creature roster and visible palette variants
    // from the client clothing tables. The initial selections keep their saved indices.
    private static IReadOnlyDictionary<RaceMountKind, MountColor[]> BuildMountColors() =>
        new Dictionary<RaceMountKind, MountColor[]>
        {
            [RaceMountKind.Gromnie] =
            [
                .. GromnieStock,
                Variation(RaceMountKind.Gromnie, "Sable", "534f57", 11527, 0x100000af, 13, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Emerald", "258010", 25597, 0x100000af, 8, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Ruby", "811010", 25598, 0x100000af, 11, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Sapphire", "176480", 25599, 0x100000af, 14, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Brass", "746121", 27710, 0x100000af, 5, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Copper", "6e5321", 27711, 0x100000af, 6, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Dark Bronze", "764e30", 0, 0x100000af, 25, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Verdigris", "44625a", 0, 0x100000af, 27, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Gold", "888010", 0, 0x100002cb, 21, 0x040001bb),
                Variation(RaceMountKind.Gromnie, "Onyx", "503810", 0, 0x100002cb, 39, 0x040001bb),
            ],
            [RaceMountKind.Cow] =
            [
                new("Black & White", "c3baaa"),
                Variation(RaceMountKind.Cow, "Brown", "c5aa8e", 618, 0x1000005e, 4, 0x04001db8, 0),
                Variation(RaceMountKind.Cow, "Holstein", "7b7b7b", 5755, 0x1000005e, 39, 0x040001b8),
                Variation(RaceMountKind.Cow, "Chestnut", "867562", 0, 0x1000005e, 4, 0x04001db8, 0, retainParts: true),
                Variation(RaceMountKind.Cow, "Moss Grey", "9ea592", 0, 0x1000005e, 14, 0x04001db8, 0, retainParts: true),
                Variation(RaceMountKind.Cow, "Brick Red", "d16665", 0, 0x1000005e, 29, 0x04001db8, 0, retainParts: true),
                Variation(RaceMountKind.Cow, "Charcoal", "737373", 0, 0x1000005e, 39, 0x04001db8, 0, retainParts: true),
                Variation(RaceMountKind.Cow, "Ebony", "101010", 0, 0x100002a8, 4, 0x04001db8, 0, retainParts: true),
                Variation(RaceMountKind.Cow, "Cream", "f2caa9", 0, 0x100006c6, 61, 0x04001db8, 0, retainParts: true),
            ],
            [RaceMountKind.Auroch] =
            [
                new("Dun Bull", "826b54"),
                Variation(RaceMountKind.Auroch, "Fire Bull", "756757", 1607, 0x1000005c, 43, 0x040001b6),
                Variation(RaceMountKind.Auroch, "Babe Blue", "4c546f", 8128, 0x1000005c, 2, 0x040001b6),
                Variation(RaceMountKind.Auroch, "Herdmaster", "865a2b", 11981, 0x1000005c, 62, 0x040001b6),
                Variation(RaceMountKind.Auroch, "Sanamar Red", "9e331d", 29504, 0x1000005c, 14, 0x040001b6),
                Variation(RaceMountKind.Auroch, "Indigo", "3b446b", 0, 0x1000005c, 2, 0x040001b6, retainParts: true),
                Variation(RaceMountKind.Auroch, "Oxide Red", "9a3017", 0, 0x1000005c, 14, 0x040001b6, retainParts: true),
                Variation(RaceMountKind.Auroch, "Sandy", "5c4421", 0, 0x1000005c, 42, 0x040001b6, retainParts: true),
                Variation(RaceMountKind.Auroch, "Sepia", "715d48", 0, 0x1000005c, 43, 0x040001b6, retainParts: true),
                Variation(RaceMountKind.Auroch, "Dark Amber", "704e1f", 0, 0x1000005c, 62, 0x040001b6, retainParts: true),
            ],
            [RaceMountKind.Chicken] =
            [
                new("Speckled", "a09c9a"),
                Variation(RaceMountKind.Chicken, "Farmyard", "84837a", 35499, 0, 0, 0x040015cf),
                Variation(RaceMountKind.Chicken, "Moss", "6f806a", 0, 0x100004b4, 2, 0x040015cf),
                Variation(RaceMountKind.Chicken, "Brown", "5b4535", 0, 0x100004b4, 4, 0x040015cf),
                Variation(RaceMountKind.Chicken, "Ash Grey", "837f7b", 0, 0x100004b4, 8, 0x040015cf),
                Variation(RaceMountKind.Chicken, "Black", "5f5858", 0, 0x100004b4, 39, 0x040015cf),
                Variation(RaceMountKind.Chicken, "Golden", "bab93a", 0, 0x100004b4, 61, 0x040015cf),
            ],
            [RaceMountKind.Rabbit] =
            [
                new("White", "bcbcbc"),
                Variation(RaceMountKind.Rabbit, "Black", "4e4c4b", 2566, 0, 0, 0),
                Variation(RaceMountKind.Rabbit, "Brown", "664743", 2567, 0x1000010d, 4, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Desert", "a27a17", 5429, 0x1000010d, 21, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Nasty Lime", "65ff80", 9531, 0x1000010d, 67, 0x040001b4, 0),
                Variation(RaceMountKind.Rabbit, "Bleached Pink", "ff99ce", 9533, 0x1000010d, 69, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Grimacing Purple", "ab17ca", 9535, 0x1000010d, 70, 0x040001b4, 0),
                Variation(RaceMountKind.Rabbit, "Twilight", "747373", 32931, 0x1000010d, 9, 0x040001bc),
                Variation(RaceMountKind.Rabbit, "Copper", "7f4010", 0, 0x1000010d, 25, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Jade", "175748", 0, 0x1000010d, 27, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Slate", "585756", 0, 0x1000010d, 51, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Dusk Blue", "9491af", 0, 0x1000010d, 53, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Mahogany", "7b3b3e", 0, 0x1000010d, 62, 0x040001b4),
                Variation(RaceMountKind.Rabbit, "Tan", "79603e", 0, 0x1000010d, 63, 0x040001b4),
            ],
            [RaceMountKind.Asheron] =
            [
                new("Arcane Blue", "53658a"),
                Variation(RaceMountKind.Asheron, "Silver Robes", "989aa2", 20193, 0x100003b2, 61, 0x04000eb2),
                Dye("Crimson", "e95b60"),
                Dye("Jade", "62c09e"),
                Dye("Amethyst", "b383ed"),
                Dye("Amber", "f0b557"),
            ],
            [RaceMountKind.Grievver] =
            [
                new("Harrower", "2b5d7f"),
                Variation(RaceMountKind.Grievver, "Scrawed", "7d927f", 7978, 0x10000246, 8, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Virulent", "8b8786", 7979, 0, 0, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Spawn", "d1a03a", 12700, 0x10000246, 17, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Tunnelling", "d24f73", 12701, 0x10000246, 14, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Spinning", "c4c4d9", 16913, 0x10000246, 61, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Bile", "376f33", 22900, 0x10000488, 8, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Traphole", "845a10", 33972, 0x10000246, 25, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Pike", "2f2f2f", 52779, 0x10000246, 39, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Verdigris", "108d7a", 0, 0x10000246, 27, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Rust", "d76752", 0, 0x10000246, 76, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Crested Indigo", "615899", 0, 0x100002a9, 5, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Crested Umber", "987f76", 0, 0x100002a9, 8, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Crested Copper", "7a4b10", 0, 0x100002a9, 25, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Crested Jade", "107a67", 0, 0x100002a9, 27, 0x04000fdf),
                Variation(RaceMountKind.Grievver, "Crested Onyx", "523a10", 0, 0x100002a9, 39, 0x04000fdf),
            ],
            [RaceMountKind.Mukkir] =
            [
                new("Brood Black", "2f2727"),
                Variation(RaceMountKind.Mukkir, "Barbaric", "445065", 31897, 0x10000645, 2, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Fanatical", "76211f", 31901, 0x10000645, 14, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Vile", "476445", 33037, 0x10000645, 8, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Coruscating", "5c4061", 33038, 0x10000645, 13, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Fledgling", "584a38", 33804, 0, 0, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Stone Grey", "58544f", 0, 0x10000645, 9, 0x04001ee3),
                Variation(RaceMountKind.Mukkir, "Umber", "60543e", 0, 0x10000645, 64, 0x04001ee3),
            ],
            [RaceMountKind.Shreth] =
            [
                new("Blood", "755653"),
                Variation(RaceMountKind.Shreth, "Hunter", "73624b", 4111, 0x10000160, 18, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Vorous", "746748", 4112, 0x10000160, 4, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Malus", "765745", 4255, 0x10000160, 42, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Gauloth", "6a6745", 7113, 0x10000490, 8, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Rendeath", "745740", 7114, 0x10000490, 14, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Fire", "664045", 26470, 0x10000160, 62, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Bronze", "7a565a", 0, 0x10000160, 40, 0x04000dfc),
                Variation(RaceMountKind.Shreth, "Iron", "626061", 0, 0x10000160, 41, 0x04000dfc),
            ],
            [RaceMountKind.Mattekar] =
            [
                new("Great", "ccc8c4"),
                Variation(RaceMountKind.Mattekar, "White", "e6e2de", 2580, 0, 0, 0),
                Variation(RaceMountKind.Mattekar, "Snowy", "d4f3eb", 2581, 0x10000111, 2, 0x04000bd5),
                Variation(RaceMountKind.Mattekar, "Dread", "656261", 9401, 0x10000111, 39, 0x04000bd5),
                Variation(RaceMountKind.Mattekar, "Tundra", "fce5d8", 12000, 0x10000111, 46, 0x04000bd5),
                Variation(RaceMountKind.Mattekar, "Filthy", "6a2f2f", 70371, 0x10000111, 14, 0x04000bd5),
                Variation(RaceMountKind.Mattekar, "Aqua", "d4f3ea", 0, 0x10000111, 22, 0x04000bd5),
            ],
            [RaceMountKind.Reedshark] =
            [
                new("Adult", "7b4a2f"),
                Variation(RaceMountKind.Reedshark, "Elder", "695c47", 18, 0x10000064, 65, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Veteran", "6c6538", 222, 0, 0, 0),
                Variation(RaceMountKind.Reedshark, "Pup", "544537", 223, 0x10000064, 64, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Dune Reaver", "6a5043", 9256, 0x10000064, 44, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Silt Lurker", "5b644e", 9257, 0x10000064, 45, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Master of the Pack", "7b3330", 12018, 0x10000064, 14, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Jungle Reaver", "3b6573", 22745, 0x10000064, 2, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Ravager", "62733b", 22746, 0x10000064, 8, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Skinner", "60574b", 24311, 0x1000048e, 39, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Stalker", "6e403a", 24312, 0x1000048e, 8, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Plaguefang", "413737", 25848, 0x100004fb, 14, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Doomshark", "5a5a5a", 25880, 0x100004fb, 39, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Hunter", "6a5047", 44047, 0x10000064, 43, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Seeker", "524438", 44049, 0x10000064, 42, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Copper", "7f4c3e", 0, 0x10000064, 25, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Verdigris", "44604e", 0, 0x10000064, 27, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Armored Copper", "814f10", 0, 0x10000392, 25, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Armored Jade", "10806e", 0, 0x10000392, 27, 0x040001c1),
                Variation(RaceMountKind.Reedshark, "Armored Onyx", "503810", 0, 0x10000392, 39, 0x040001c1),
            ],
            [RaceMountKind.BaelZharon] =
            [
                new("Hopeslayer", "635164"),
                Dye("Ember", "ed8553"),
                Dye("Frost", "6fcde9"),
                Dye("Void Violet", "b981ee"),
                Dye("Venom", "81c66e"),
            ],
            [RaceMountKind.FestivusCarpet] =
            [
                new("Festivus Red", "b74857"),
                Dye("Lapis", "6b9de7"),
                Dye("Jade", "71c99f"),
                Dye("Amethyst", "bc86db"),
                Dye("Marigold", "eec154"),
            ],
            [RaceMountKind.IsinDuleDisc] =
            [
                new("Isin Dule Gold", "b99c58"),
                Dye("Cobalt", "6aa2f0"),
                Dye("Emerald", "68d0a0"),
                Dye("Rose", "e77491"),
                Dye("Silver", "bdcdd9"),
            ],
            [RaceMountKind.CarenziSkates] =
            [
                new("Red & Black", "995032"),
                Variation(RaceMountKind.CarenziSkates, "Rust Red", "af7a5c", 38945, 0x100004fc, 14, 0x040016e2),
                Variation(RaceMountKind.CarenziSkates, "Snarl Black", "4f4341", 25844, 0x100004fc, 39, 0x040016e2),
                Variation(RaceMountKind.CarenziSkates, "Ravager Purple", "543d65", 25852, 0x100004fc, 53, 0x040016e2),
                Variation(RaceMountKind.CarenziSkates, "Ochre", "b06c40", 38946, 0x100004fc, 0, 0),
                Variation(RaceMountKind.CarenziSkates, "Slate", "8a8684", 38947, 0x100004fc, 21, 0x040016e2),
                Variation(RaceMountKind.CarenziSkates, "Ember", "993a29", 38951, 0x100004fc, 46, 0x040016e2),
            ],
            [RaceMountKind.Siraluun] =
            [
                .. SiraluunColors.Select(v => new MountColor(v.Name, v.Hex, v.Look)),
                Variation(RaceMountKind.Siraluun, "Dusk", "5b4a50", 0, 0x100002e2, 2, 0x0400111f),
            ],
        };
}
