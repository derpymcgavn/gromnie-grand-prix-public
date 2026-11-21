using Godot;
using DerethSurvivors;
using System.Runtime.CompilerServices;

internal readonly record struct MountColor(string Name, string Hex, RareCatalog.Creature? Look = null, Color? Tint = null);

internal sealed partial record RaceMount
{
    private static readonly Lazy<IReadOnlyDictionary<RaceMountKind, MountColor[]>> MountColors = new(BuildMountColors);
    private static readonly ConditionalWeakTable<Texture2D, Dictionary<Color, Texture2D>> DyedTextures = new();

    internal static IReadOnlyList<MountColor> ColorsFor(RaceMountKind kind) => MountColors.Value[kind];

    private MountColor ResolveColor(int color)
    {
        var colors = ColorsFor(Kind);
        return colors[Math.Clamp(color, 0, colors.Count - 1)];
    }

    internal RareCatalog.Creature ResolveSkateCompanion(int color) =>
        color == 0 ? CarenziRacer(38948, 39) : ResolveColor(color).Look ?? Native!;

    internal void ApplyMountColor(AcActor actor, RareCatalog.Creature look, int color)
    {
        actor.ApplyNativeAppearance(look);
        if (ResolveColor(color).Tint is not { } tint) return;

        var dyedMaterials = new Dictionary<StandardMaterial3D, StandardMaterial3D>();
        foreach (var (mesh, part) in actor.BodyMeshBindings)
        {
            // Change Asheron's robe without dyeing his face, hands or boots.
            if (!mesh.Visible || Kind == RaceMountKind.Asheron && part is not (0 or 1 or 5 or 9 or 11 or 14)) continue;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D original) continue;
                if (!dyedMaterials.TryGetValue(original, out var dyed))
                {
                    dyed = (StandardMaterial3D)original.Duplicate();
                    if (original.AlbedoTexture is { } texture)
                    {
                        dyed.AlbedoTexture = DyeTexture(texture, tint);
                        dyed.AlbedoColor = original.AlbedoColor * Colors.White.Lerp(tint, .12f);
                    }
                    else dyed.AlbedoColor = original.AlbedoColor * tint;
                    dyedMaterials.Add(original, dyed);
                }
                mesh.SetSurfaceOverrideMaterial(surface, dyed);
            }
        }
    }

    private static Texture2D DyeTexture(Texture2D texture, Color tint)
    {
        var variants = DyedTextures.GetOrCreateValue(texture);
        if (variants.TryGetValue(tint, out var cached)) return cached;
        using var source = texture.GetImage();
        if (source.IsCompressed()) source.Decompress();
        source.ClearMipmaps();
        source.Convert(Image.Format.Rgba8);
        byte[] pixels = source.GetData();
        Color dye = Colors.White.Lerp(tint, .86f);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            float value = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
            float highlight = Mathf.SmoothStep(202, 255, value) * .34f;
            pixels[i] = (byte)Mathf.RoundToInt(value * Mathf.Lerp(dye.R, 1, highlight));
            pixels[i + 1] = (byte)Mathf.RoundToInt(value * Mathf.Lerp(dye.G, 1, highlight));
            pixels[i + 2] = (byte)Mathf.RoundToInt(value * Mathf.Lerp(dye.B, 1, highlight));
        }
        using var image = Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, Image.Format.Rgba8, pixels);
        image.GenerateMipmaps();
        return variants[tint] = ImageTexture.CreateFromImage(image);
    }

    private static MountColor Variation(RaceMountKind kind, string name, string hex, int id, uint clothing,
        uint color, uint palette, float shade = .5f, bool retainParts = false)
    {
        var basis = Get(kind).Native;
        return new(name, hex, new RareCatalog.Creature
        {
            Id = id != 0 ? id : basis?.Id ?? 1612,
            Name = name,
            Setup = basis?.Setup ?? 0x02000037u,
            Motion = basis?.Motion ?? 0x0900001bu,
            Sound = basis?.Sound ?? 0x20000009u,
            Palette = palette,
            Clothing = clothing,
            PaletteTemplate = color,
            Shade = shade,
            Appearance = retainParts && basis is not null
                ? basis.Appearance.Where(row => row.Key != "weenie_properties_palette").ToDictionary() : new()
        });
    }

    private static MountColor Dye(string name, string hex) => new(name, hex, Tint: new Color(hex));
}
