# Run the GGPrix relay on Windows

Extract the whole ZIP to a folder on the Windows x64 server, then double-click **Start-GGPrix-Server.cmd**. Leave the console open while people play. Press **Ctrl+C** to stop it.

The ZIP includes .NET and ASP.NET Core. The relay runs without installing either runtime or copying the game or DAT files to the server.

## Connect players

1. For internet play, set up the one-time hostname route from `ggprix.derptide.net` to `http://127.0.0.1:8040` in the HTTPS proxy or Cloudflare Tunnel on the same server. See **DERPTIDE-HOSTNAME.md** for both configurations. The ZIP includes the Caddy site block.
2. Run **Check-GGPrix-Connection.cmd** while the relay is running to check public HTTPS health and room listings, plus the WebSocket used by racers.
3. In the game, open **Play Online**, select **Host Race**, and share the eight-character invite code. Friends on the default relay can paste that code directly. Each player connects to the relay, so the race host's home IP stays private from the other racers.

## Local testing and settings

The local health address is `http://127.0.0.1:8040/health`. When the game is running on the server box, use `ws://127.0.0.1:8040/race` in **Relay Settings**. To check all three routes locally, run the following command while the relay is running.

```powershell
.\Check-GGPrix-Connection.cmd -CheckUrl http://127.0.0.1:8040
```

Edit **GGPrix-Server.json** to change the loopback port or the public URL the check uses. Set the proxy or tunnel origin to the same port. For a one-time port override, run `.\Start-GGPrix-Server.cmd -Port 8041`.

Run a single relay instance for this hostname. Rooms are kept in memory, so players need a new invite after a restart.
