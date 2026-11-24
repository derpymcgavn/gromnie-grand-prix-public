using Godot;

public partial class GrandPrix
{
    private const uint RegattaSilverScopeSurface = 0x08001559;

    private Texture2D? RegattaPlateTexture() => assets.Archive.Entries.ContainsKey(RegattaSilverScopeSurface)
        ? assets.Surface(RegattaSilverScopeSurface).AlbedoTexture : RegattaStoneTexture();

    private Texture2D? RegattaStoneTexture()
    {
        var cobble = townRegion?.Blend?.Textures.FirstOrDefault(layer => layer.Type == 32);
        return cobble is null ? null : assets.Texture(cobble.Texture);
    }

    private static StandardMaterial3D RegattaFinish(Texture2D? texture, string tint, float roughness, float glow = 0)
    {
        var material = BogCircuit.Paint(tint, roughness);
        material.AlbedoTexture = texture;
        if (glow > 0)
        {
            material.EmissionEnabled = true;
            material.Emission = new Color(tint);
            material.EmissionTexture = texture;
            material.EmissionEnergyMultiplier = glow;
        }
        return material;
    }
}
