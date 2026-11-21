using System.Globalization;
using System.Text.Json;

namespace GromnieOnline;

public sealed record Wire(string Type, JsonElement? Data = null, string? Code = null, int From = 0, int Revision = 0)
{
    public const int Protocol = 1;
    public const int Capacity = 5;
    public const int MaxBytes = 65_536;
    public const int ChatMaxLength = 160;
    public const int ChatIntervalMs = 500;
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static Wire Packet<T>(string type, T data, int revision = 0) => new(type, JsonSerializer.SerializeToElement(data), Revision: revision);
    public T? Read<T>() => Data is { } data ? data.Deserialize<T>() : default;

    public static string CleanCode(string code) => code.Trim().Replace("-", "").ToUpperInvariant();
    public static bool IsCode(string code) => code.Length == 8 && code.All(CodeAlphabet.Contains);

    public static bool TryChatText(string? text, out string message)
    {
        message = text?.Trim() ?? "";
        return message.Length is > 0 and <= ChatMaxLength && !message.Any(IsNonprintingChatCharacter);
    }

    private static bool IsNonprintingChatCharacter(char value) => char.GetUnicodeCategory(value) switch
    {
        UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => true,
        UnicodeCategory.Format => value is not ('\u200c' or '\u200d'),
        _ => false
    };
}

public sealed record RelayMember(int Id, JsonElement Profile);
public sealed record RelayRoster(string Code, int You, int Revision, bool Locked, RelayMember[] Members, bool ChatEnabled = false);
public sealed record Admit(int Protocol, JsonElement Profile, RaceListing? Listing = null);
public sealed record RaceListing(bool Public, int Course);
public sealed record PublicRoom(string Code, string Name, int Course, int Players, int Capacity);
public sealed record PublicRoomList(int Protocol, PublicRoom[] Rooms);
