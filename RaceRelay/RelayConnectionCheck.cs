using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using GromnieOnline;

internal static class RelayConnectionCheck
{
    public static async Task<int> Run(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var root) || root.AbsolutePath != "/" ||
            root.UserInfo.Length != 0 || root.Query.Length != 0 || root.Fragment.Length != 0 ||
            !(root.Scheme == "https" || root.IsLoopback && root.Scheme == "http"))
        {
            Console.Error.WriteLine("Specify an HTTPS relay base URL or a local loopback HTTP URL.");
            return 1;
        }

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new ClientWebSocket();
        string route = "/health";
        try
        {
            var health = await client.GetFromJsonAsync<JsonElement>(new Uri(root, route), timeout.Token);
            if (health.GetProperty("ready").ValueKind != JsonValueKind.True ||
                health.GetProperty("protocol").GetInt32() != Wire.Protocol)
                throw new InvalidDataException("Relay readiness or protocol did not match.");

            route = "/rooms";
            var rooms = await client.GetFromJsonAsync<PublicRoomList>(new Uri(root, route),
                JsonSerializerOptions.Default, timeout.Token);
            if (rooms?.Protocol != Wire.Protocol || rooms.Rooms is null)
                throw new InvalidDataException("Relay room directory did not match the current protocol.");

            route = "/race";
            var socketUrl = new UriBuilder(new Uri(root, route))
                { Scheme = root.Scheme == "https" ? "wss" : "ws", Port = root.Port }.Uri;
            await socket.ConnectAsync(socketUrl, timeout.Token);
            byte[] ping = JsonSerializer.SerializeToUtf8Bytes(new Wire("ping"));
            await socket.SendAsync(ping.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            byte[] buffer = new byte[Wire.MaxBytes];
            int length = 0;
            ValueWebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
                length += part.Count;
                if (part.MessageType != WebSocketMessageType.Text || length >= buffer.Length)
                    throw new InvalidDataException("Relay did not send a valid response.");
            } while (!part.EndOfMessage);
            if (JsonSerializer.Deserialize<Wire>(buffer.AsSpan(0, length))?.Type != "pong")
                throw new InvalidDataException("Relay did not answer the WebSocket ping.");

            Console.WriteLine($"Relay reachable at {root} (/health, /rooms, /race). Protocol {Wire.Protocol}.");
            return 0;
        }
        catch (Exception error) when (error is HttpRequestException or WebSocketException or
            OperationCanceledException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"Failed to check {new Uri(root, route)}. {error.Message}");
            return 1;
        }
    }
}
