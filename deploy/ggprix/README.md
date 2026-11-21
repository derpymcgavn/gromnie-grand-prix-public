# Gromnie Grand Prix relay on Derptide

The race host and guests connect outbound to `wss://ggprix.derptide.net/race`. The relay forwards the room traffic. Players connect to the relay endpoint, so the race host can stay behind their router with no inbound port needed. The relay runs independently of the game and DAT files.

## Start the relay

On Windows x64, extract the Windows relay package on your server box and open `Start-GGPrix-Server.cmd`. It includes its own .NET and ASP.NET Core runtime and listens at `http://127.0.0.1:8040`. To build the package from source, run `./deploy/Publish-GGPrix-Relay.ps1 -Destination ./relay-release` from the repository root on Windows with the .NET SDK and current ASP.NET Core 8 x64 runtime installed.

For a Docker host, run this from the repository root.

```sh
docker compose -f deploy/ggprix/compose.yaml up -d --build
curl -fsS http://127.0.0.1:8040/health
```

The Compose service uses the existing RaceRelay image build and binds its published port to loopback. Run one relay instance because rooms are held in memory. Restarting the relay closes active rooms. [Docker port publishing](https://docs.docker.com/engine/network/port-publishing/)

## Publish the hostname

With an existing Cloudflare Tunnel on the relay box, add a public hostname for `ggprix.derptide.net` and set its service to `http://127.0.0.1:8040`. Use the dashboard’s published application route for a remotely managed tunnel. For a locally managed tunnel, insert the following entry ahead of the final catch-all in your current `ingress` list and route the hostname’s DNS to that tunnel. Cloudflare handles public HTTPS and WebSockets, so the relay box can keep its public inbound ports closed. [Tunnel configuration](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/local-management/configuration-file/) · [Tunnel DNS](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/routing-to-tunnel/dns/) · [Cloudflare WebSockets](https://developers.cloudflare.com/network/websockets/)

```yaml
  - hostname: ggprix.derptide.net
    service: http://127.0.0.1:8040
```

With an existing HTTPS reverse proxy on the relay box, route `/health`, `/rooms`, and WebSocket upgrades for `/race` to the same loopback listener. The included `Caddyfile` is a site block for Caddy. When using direct DNS, point the hostname at the proxy and allow it to complete certificate validation. Direct DNS exposes the proxy’s address. [Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy) · [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https)

## Verify from a client

On Windows, run **Check-GGPrix-Connection.cmd** from the ZIP. It checks HTTPS, the relay protocol, and the WebSocket handshake with the relay.

```sh
curl -fsS https://ggprix.derptide.net/health
curl -fsS https://ggprix.derptide.net/rooms
```

The response should report `ready: true` and the relay protocol version. The game defaults to `wss://ggprix.derptide.net/race`. Open **Play Online**, choose **Host Race**, and click **Copy Invite** for the eight-character code. A friend using the default relay pastes that code under **Join Race**.

The room list reports the protocol version and an empty `Rooms` array until an open public room has space to join. Update the relay package and the proxy routes together when enabling public browsing on an existing deployment.
