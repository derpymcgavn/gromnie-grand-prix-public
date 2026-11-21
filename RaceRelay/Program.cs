using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using GromnieOnline;

if (args is ["--check", var url]) return await RelayConnectionCheck.Run(url);

var builder = WebApplication.CreateSlimBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]) &&
    string.IsNullOrWhiteSpace(builder.Configuration["http_ports"]) &&
    string.IsNullOrWhiteSpace(builder.Configuration["https_ports"]))
    builder.WebHost.UseUrls("http://127.0.0.1:8040");
builder.WebHost.ConfigureKestrel(settings =>
{
    settings.AddServerHeader = false;
    settings.Limits.MaxConcurrentUpgradedConnections = 192;
});
var app = builder.Build();
var relay = new Rooms();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Ok(new { ready = true, protocol = Wire.Protocol }));
app.MapGet("/rooms", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(relay.ListPublicRooms(), JsonSerializerOptions.Default);
});
app.MapGet("/race", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await relay.Serve(socket, context.RequestAborted);
});
app.Run();
return 0;

internal sealed class Rooms
{
    private sealed class Guest(WebSocket socket, CancellationToken cancellation) : IDisposable
    {
        internal readonly WebSocket Socket = socket;
        internal readonly CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        internal readonly Channel<byte[]> Outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(96)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        internal Lobby? Lobby;
        internal int Id;
        internal long NextChatAt;
        internal JsonElement Profile;

        public void Dispose() => Stop.Dispose();
    }

    private sealed class Lobby(string code, RaceListing? listing)
    {
        internal readonly string Code = code;
        internal readonly List<Guest> Guests = [];
        internal int LastId, Revision;
        internal bool Locked;
        internal RaceListing? Listing = listing;
    }

    private readonly object sync = new();
    private readonly Dictionary<string, Lobby> lobbies = new(StringComparer.Ordinal);

    private static readonly string[] RoomNames =
    [
        "Gromnie Gallop", "Tusker Trot", "Olthoi Circuit", "Mosswart Dash",
        "Drudge Derby", "Banderling Blitz", "Shreth Rally", "Golem Grand Prix"
    ];

    internal PublicRoomList ListPublicRooms()
    {
        lock (sync)
        {
            var rooms = new List<PublicRoom>();
            foreach (var lobby in lobbies.Values)
            {
                if (lobby.Listing is not { Public: true } listing || lobby.Locked || lobby.Guests.Count >= Wire.Capacity)
                    continue;
                int nameIndex = Wire.CodeAlphabet.IndexOf(lobby.Code[0]) % RoomNames.Length;
                string name = $"{RoomNames[nameIndex]} · {lobby.Code[^3..]}";
                rooms.Add(new PublicRoom(lobby.Code, name, listing.Course, lobby.Guests.Count, Wire.Capacity));
            }
            return new PublicRoomList(Wire.Protocol, rooms.ToArray());
        }
    }

    internal async Task Serve(WebSocket socket, CancellationToken cancellation)
    {
        using var guest = new Guest(socket, cancellation);
        var writer = SendMessages(guest);
        var buffer = new byte[Wire.MaxBytes];
        long window = Environment.TickCount64;
        int count = 0;
        try
        {
            while (!guest.Stop.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(guest.Stop.Token);
                limit.CancelAfter(TimeSpan.FromSeconds(100));
                int size = 0;
                WebSocketReceiveResult part;
                do
                {
                    if (size == buffer.Length) return;
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, size, buffer.Length - size), limit.Token);
                    if (part.MessageType != WebSocketMessageType.Text) return;
                    size += part.Count;
                }
                while (!part.EndOfMessage);
                if (Environment.TickCount64 - window >= 1000) { count = 0; window = Environment.TickCount64; }
                if (++count > 120) return;
                if (JsonSerializer.Deserialize<Wire>(buffer.AsSpan(0, size)) is not { } wire) return;
                lock (sync) Dispatch(guest, wire);
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or JsonException or ArgumentException) { }
        finally
        {
            lock (sync) Leave(guest);
            guest.Outbox.Writer.TryComplete();
            guest.Stop.Cancel();
            await writer;
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Connection ended", closeLimit.Token); }
                catch (Exception error) when (error is WebSocketException or OperationCanceledException) { }
            }
        }
    }

    private static async Task SendMessages(Guest guest)
    {
        try
        {
            await foreach (var bytes in guest.Outbox.Reader.ReadAllAsync(guest.Stop.Token))
                await guest.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, guest.Stop.Token);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        { guest.Stop.Cancel(); }
    }

    private void Dispatch(Guest sender, Wire wire)
    {
        if (wire.Type == "ping") { Send(sender, new Wire("pong")); return; }
        if (sender.Lobby is not { } room)
        {
            if (wire.Type is "host" or "join") AdmitToRoom(sender, wire);
            return;
        }

        if (wire.Type == "leave") { Leave(sender); return; }
        bool host = sender.Id == 1;
        switch (wire.Type)
        {
            case "chat" when !room.Locked:
                RelayChat(room, sender, wire);
                break;
            case "listing" when host && !room.Locked:
                if (wire.Data is { ValueKind: JsonValueKind.Object } && wire.Read<RaceListing>() is { } listing)
                    room.Listing = listing;
                break;
            case "prepare" when host && !room.Locked:
                if (wire.Revision != room.Revision) { Error(sender, "The roster changed. Start the race again."); return; }
                room.Locked = true;
                Forward(room, sender, wire);
                Announce(room);
                break;
            case "loaded" or "input" when !host && room.Locked:
                Send(room.Guests[0], wire with { From = sender.Id, Code = null, Revision = 0 });
                break;
            case "go" or "state" when host && room.Locked:
                Forward(room, sender, wire, includeSender: false);
                break;
            case "lobby" when host && room.Locked:
                room.Locked = false;
                Forward(room, sender, wire);
                Announce(room);
                break;
        }
    }

    private static void RelayChat(Lobby room, Guest sender, Wire wire)
    {
        long now = Environment.TickCount64;
        if (now < sender.NextChatAt || wire.Data is not { ValueKind: JsonValueKind.String }) return;
        if (!Wire.TryChatText(wire.Read<string>(), out string text)) return;

        sender.NextChatAt = now + Wire.ChatIntervalMs;
        Forward(room, sender, Wire.Packet("chat", text));
    }

    private void AdmitToRoom(Guest guest, Wire wire)
    {
        if (wire.Data is not { ValueKind: JsonValueKind.Object } || wire.Data.Value.GetRawText().Length > 10500)
        { Error(guest, "Invalid rider profile."); return; }
        var request = wire.Read<Admit>();
        if (request is null || request.Protocol != Wire.Protocol || request.Profile.ValueKind != JsonValueKind.Object)
        { Error(guest, "Update the game before joining."); return; }
        Lobby? room;
        if (wire.Type == "host")
        {
            if (lobbies.Count >= 48) { Error(guest, "The relay is full. Try again shortly."); return; }
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 8).Select(_ => Wire.CodeAlphabet[RandomNumberGenerator.GetInt32(Wire.CodeAlphabet.Length)]).ToArray());
            } while (lobbies.ContainsKey(code));
            room = new Lobby(code, request.Listing);
            lobbies.Add(code, room);
        }
        else
        {
            string code = Wire.CleanCode(wire.Code ?? "");
            if (!Wire.IsCode(code) || !lobbies.TryGetValue(code, out room))
            { Error(guest, "Invite code not found."); return; }
            if (room.Locked) { Error(guest, "This race has already started."); return; }
            if (room.Guests.Count >= Wire.Capacity) { Error(guest, "This lobby is full."); return; }
        }
        guest.Lobby = room;
        guest.Id = ++room.LastId;
        guest.Profile = request.Profile;
        room.Guests.Add(guest);
        room.Revision++;
        Announce(room);
    }

    private void Leave(Guest guest)
    {
        if (guest.Lobby is not { } room) return;
        guest.Lobby = null;
        room.Guests.Remove(guest);
        if (guest.Id != 1)
        {
            room.Revision++;
            Announce(room);
            return;
        }
        lobbies.Remove(room.Code);
        foreach (var member in room.Guests)
        {
            member.Lobby = null;
            Send(member, Wire.Packet("closed", "The host left the lobby."));
        }
        room.Guests.Clear();
    }

    private static void Announce(Lobby lobby)
    {
        RelayMember[] roster = lobby.Guests.Select(guest => new RelayMember(guest.Id, guest.Profile)).ToArray();
        foreach (var guest in lobby.Guests)
            Send(guest, Wire.Packet("room", new RelayRoster(lobby.Code, guest.Id, lobby.Revision, lobby.Locked, roster, ChatEnabled: true)));
    }

    private static void Forward(Lobby lobby, Guest sender, Wire wire, bool includeSender = true)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wire with { From = sender.Id, Code = null });
        foreach (var guest in lobby.Guests)
            if (includeSender || guest != sender) Enqueue(guest, bytes);
    }

    private static void Error(Guest guest, string message) => Send(guest, Wire.Packet("error", message));
    private static void Send(Guest guest, Wire wire) => Enqueue(guest, JsonSerializer.SerializeToUtf8Bytes(wire));
    private static void Enqueue(Guest guest, byte[] bytes)
    {
        if (bytes.Length > Wire.MaxBytes || !guest.Outbox.Writer.TryWrite(bytes)) guest.Stop.Cancel();
    }
}
