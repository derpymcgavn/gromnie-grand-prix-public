using DerethSurvivors;
using System.Net.Sockets;

public partial class GrandPrix
{
    private CancellationTokenSource? nativeAceLifetime;
    private AceNativeSession? nativeAceSession;
    private bool nativeAceBusy;

    private static bool TryAceServer(string address, out string host, out int port)
    {
        host = "";
        port = 9000;
        address = address.Trim();
        if (address.Length is 0 or > 260 || !Uri.TryCreate("udp://" + address, UriKind.Absolute, out var server) ||
            server.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4) || server.UserInfo.Length != 0 ||
            server.AbsolutePath != "/" || server.Query.Length != 0 || server.Fragment.Length != 0 || server.Port is 0 or >= 65535)
            return false;
        host = server.IdnHost;
        if (server.Port > 0) port = server.Port;
        return true;
    }

    private async void ConnectNativeAce()
    {
        if (nativeAceBusy) return;
        if (!TryAceServer(nativeAceAddress.Text, out string host, out int port))
        {
            SetNativeAceBusy(false, "Enter your shard’s game address, for example shard.example:9000.");
            return;
        }
        string account = nativeAceAccount.Text.Trim();
        string password = nativeAcePassword.Text;
        if (account.Length is < 1 or > 50 || password.Length is < 1 or > 254 ||
            !account.All(PrintableAscii) || !password.All(PrintableAscii))
        {
            SetNativeAceBusy(false, "Enter the account name and password you use to sign in to this shard.");
            return;
        }

        CancelNativeAce();
        nativeAcePassword.Clear();
        var attempt = new CancellationTokenSource();
        nativeAceLifetime = attempt;
        var token = attempt.Token;
        string folder = datFolder;
        SetNativeAceCharacters([]);
        SetNativeAceBusy(true, "Signing in and loading your character list…");
        try
        {
            byte[] datInfo = await Task.Run(() => AceNativeDatInfo.Read(folder, token), token);
            var connection = AceNativeSession.ConnectAsync(host, port, account, password, datInfo, token);
            password = "";
            var session = await connection;
            if (nativeAceLifetime != attempt)
            {
                await DisposeNativeAce(session);
                return;
            }
            nativeAceSession = session;
            SetNativeAceCharacters(session.Characters.Select(character => character.Name));
            SetNativeAceBusy(false, session.Characters.Count == 0
                ? "This account has no available characters on the shard."
                : "Choose the character whose current outfit you want to import.");
            if (session.Characters.Count == 0) CancelNativeAce();
        }
        catch (Exception error) when (IsNativeAceError(error))
        {
            if (nativeAceLifetime != attempt) return;
            CancelNativeAce();
            SetNativeAceBusy(false, NativeAceErrorText(error));
        }
    }

    private async void ImportNativeAceCharacter()
    {
        if (nativeAceBusy || nativeAceSession is not { } session || nativeAceLifetime is not { } attempt ||
            SelectedNativeAceCharacterIndex() is not { } index || index < 0 || index >= session.Characters.Count)
            return;
        var character = session.Characters[index];
        SetNativeAceBusy(true, $"Loading {character.Name}’s appearance from the shard…");
        string status;
        try
        {
            var snapshot = await AceNativeAppearance.ImportAsync(session, character.Id, character.Name, attempt.Token);
            if (nativeAceLifetime != attempt) return;
            TryApplyImportedRider(snapshot, out status);
        }
        catch (Exception error) when (IsNativeAceError(error))
        {
            if (nativeAceLifetime != attempt) return;
            status = NativeAceErrorText(error);
        }
        if (nativeAceLifetime != attempt) return;
        nativeAceSession = null;
        await DisposeNativeAce(session);
        if (nativeAceLifetime != attempt) return;
        CancelNativeAce();
        SetNativeAceCharacters([]);
        SetNativeAceBusy(false, status);
    }

    private void CancelNativeAce()
    {
        var attempt = nativeAceLifetime;
        nativeAceLifetime = null;
        attempt?.Cancel();
        attempt?.Dispose();
        var session = nativeAceSession;
        nativeAceSession = null;
        nativeAceBusy = false;
        if (session is not null) _ = DisposeNativeAce(session);
    }

    private static async Task DisposeNativeAce(AceNativeSession session)
    {
        try { await session.DisposeAsync(); }
        catch (Exception error) when (IsNativeAceError(error)) { }
    }

    private static bool PrintableAscii(char value) => value is >= ' ' and <= '~';

    private static bool IsNativeAceError(Exception error) => error is IOException or InvalidDataException or SocketException or
        OperationCanceledException or TimeoutException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or
        KeyNotFoundException or OverflowException or FormatException;

    private static string NativeAceErrorText(Exception error) => error switch
    {
        OperationCanceledException or TimeoutException => "The shard did not respond in time. Check its address and try again.",
        SocketException => "Could not reach the shard. Check its address and game port.",
        UnauthorizedAccessException => "Could not access the game’s DAT files.",
        IOException or InvalidDataException or InvalidOperationException => error.Message,
        _ => "Could not complete the native ACE import."
    };
}
