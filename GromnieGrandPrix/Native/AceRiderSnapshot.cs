using System.Net;
using System.Text.Json.Serialization;

namespace DerethSurvivors;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record AceRiderSnapshot(
    [property: JsonRequired] int Version, [property: JsonRequired] string Name,
    [property: JsonRequired] uint Setup, [property: JsonRequired] uint Motion, uint Sound, float? Scale, uint Palette,
    [property: JsonRequired] CharacterPaletteChange[] Palettes,
    [property: JsonRequired] CharacterTextureChange[] Textures,
    [property: JsonRequired] CharacterPartChange[] Parts)
{
    internal const int MaxResponseBytes = 65_536;

    internal static bool TryEndpoint(string address, out Uri? endpoint)
    {
        if (Uri.TryCreate(address.Trim(), UriKind.Absolute, out endpoint) &&
            (endpoint.Scheme == Uri.UriSchemeHttps ||
             endpoint.Scheme == Uri.UriSchemeHttp && IPAddress.TryParse(endpoint.DnsSafeHost, out var ip) && IPAddress.IsLoopback(ip)) &&
            endpoint.Host.Length > 0 && endpoint.UserInfo.Length == 0 &&
            endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0)
            return true;
        endpoint = null;
        return false;
    }

    internal static bool IsPairingCode(string code) => code.Length == 20 && code.All(Uri.IsHexDigit);

    internal CharacterLookDefinition Resolve(AcAssets assets)
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Name.Any(char.IsControl) ||
            Scale is { } scale && (!float.IsFinite(scale) || scale <= 0) ||
            Palettes is not { Length: <= 255 } || Textures is not { Length: <= 255 } || Parts is not { Length: <= 255 })
            throw new InvalidDataException("The imported rider appearance is invalid or unsupported.");

        CheckAsset(Setup, 2);
        CheckAsset(Motion, 9);
        CheckAsset(Sound, 0x20, optional: true);
        CheckAsset(Palette, 4, optional: true);
        int partCount = assets.Setup(Setup).Parts.Length;
        foreach (var p in Palettes)
        {
            if (p.Count is < 8 or > 2048 || p.Offset < 0 || p.Offset > 2048 - p.Count ||
                p.Offset % 8 != 0 || p.Count % 8 != 0)
                throw new InvalidDataException("The imported appearance has an invalid dye range.");
            CheckAsset(p.Palette, 4);
        }
        foreach (var t in Textures)
        {
            CheckPart(t.Part);
            CheckAsset(t.OldTexture, 5);
            CheckAsset(t.NewTexture, 5);
        }
        foreach (var p in Parts)
        {
            CheckPart(p.Part);
            CheckAsset(p.Model, 1, optional: true);
        }
        return new(0, 0, Setup, Motion, Sound, Scale ?? 1, Palette, new(Palette, Palettes, Textures, Parts), []);

        void CheckPart(int index)
        {
            if (index < 0 || index >= partCount)
                throw new InvalidDataException("The imported appearance uses a part outside the rider's native body.");
        }

        void CheckAsset(uint id, uint type, bool optional = false)
        {
            if (optional && id == 0) return;
            if (id >> 24 != type || !assets.Archive.Entries.ContainsKey(id))
                throw new InvalidDataException($"Your DATs are missing a required rider asset ({id:X8}).");
        }
    }
}
