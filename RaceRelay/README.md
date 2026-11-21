# Invite-code multiplayer

Open **Play Online** from the stable. Pick a display name, then choose **Host Race**. Use **Copy Invite** to share the room. Friends paste the invite into the **Invite code** field and choose **Join Race**. The host chooses the course and starts. The countdown waits until every racer has loaded the track.

The default relay is `wss://ggprix.derptide.net/race`. Once it is online, friends on that relay can share just the eight-character code. Invites from custom relays include their address so guests can join with one paste. A room holds up to five human racers and fills empty slots with AI. The host can share a saved track from the editor with everyone in the room.

Hosts can make their rooms public so other racers can find them in **Play Online**. Rooms start private by default. The browser shows public rooms with space to join. The relay removes entries when a race starts or its host closes the room. Each entry uses a generated room name and shows the selected course, invite code, and player count.

The host runs vehicle movement, checkpoints, pickups, hits, shields and finish times. Every player opens an outbound WebSocket to the relay, which forwards session messages using room member IDs. Players in a room receive the relay address and their peers’ in-game data. The relay operator and any TLS provider can see connection IPs. Each game loads its terrain and models from local DATs.

## Run a relay

On a Windows x64 server box, use [the portable Windows launcher](../deploy/ggprix-windows/README.md). It starts the relay with one double-click and includes a check for the public hostname.

For local two-client testing, run the following in the repository root. In **Relay Settings**, use `ws://127.0.0.1:8040/race`.

```powershell
dotnet run --project RaceRelay -- --urls http://127.0.0.1:8040
```

For internet play, deploy the `RaceRelay` folder to a web host or build its Dockerfile. The container listens on port 8080. Put it behind a WebSocket-capable HTTPS proxy with a trusted certificate and point the game at `wss://your-relay.example/race`. `GET /health` reports readiness and protocol version. Route `GET /rooms` to the same relay for public room browsing. The list is served with `Cache-Control: no-store`.

Set `GROMNIE_RELAY` or put a URL in `GromnieGrandPrix/RelayUrl.txt` to override the preset. Only the host needs this step when sharing a full invite. See [Dedicated relay deployment](../deploy/ggprix/README.md) for hostname and HTTPS setup.

The relay keeps rooms and invite codes in memory. Rooms close when the host leaves or the relay restarts. Run a single instance or keep each room’s WebSocket sessions on the same instance.

## Host an internet playtest from Windows

Open `RaceRelay/HostInternet.cmd` to start a loopback relay and a temporary HTTPS tunnel, then launch the game. The launcher looks for `RaceRelay/tools/cloudflared.exe`, then checks `PATH`. Use `-Cloudflared` to supply a different path. The launcher also accepts `-Godot` and `-DatFolder`. Choose **Play Online**, host, and copy the invite. Guests use their regular game launcher. Closing the host game ends the temporary relay. [Cloudflared downloads](https://developers.cloudflare.com/tunnel/downloads/) · [Cloudflare Quick Tunnels](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/)
