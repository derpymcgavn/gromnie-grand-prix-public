# Live ACE rider import

Gromnie Grand Prix imports the visual model your ACE character is wearing when you run `/gromnielook`. The bridge uses ACE's own model description, including the character's face, armor geometry, textures, and dye palettes. The racer resolves the art from your installed DATs and supplies its own mount and race weapon.

## Enable it on a shard

1. Copy `GromnieLookCommands.cs` into `Source/ACE.Server/Command/Handlers` in the ACE or DerpACE source tree. Add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to an `ItemGroup` in `Source/ACE.Server/ACE.Server.csproj`. Build and deploy as usual. A framework-dependent deployment needs the ASP.NET Core runtime that matches ACE's .NET version.
2. Set `GROMNIE_APPEARANCE_PORT=39172` in the ACE process environment and restart it. The listener starts on the first use of `/gromnielook` and binds to `127.0.0.1`. Choose any free port.
3. For other machines, add `/rider-appearance` to your shard's HTTPS reverse proxy and forward that path and the Authorization header to `http://127.0.0.1:39172/rider-appearance`. Use a trusted HTTPS certificate. Keep Authorization headers out of proxy logs.

## Import from the stable

1. Enter the world with your character and wear the outfit you want to race in. Type `/gromnielook` in AC chat.
2. In the stable, choose **Import from ACE**. Enter the shard operator's HTTPS appearance endpoint and the code from AC chat. If ACE and the racer run on the same machine, use `http://127.0.0.1:39172/rider-appearance`.
3. Choose **Import**. The imported rider is saved locally and works offline. Issue a new code to pull in your latest outfit, or choose **Use custom rider** to return to your stable design.

Codes are sent privately, expire after five minutes, and can be redeemed once. Requesting a new code replaces your old one. The bridge exports a visual snapshot for the signed-in player who ran the command. It needs no game password and exposes no character-name lookup. An outfit using shard-specific art requires matching client DATs on the racing PC.

## Test locally

With the .NET 10 SDK, run `dotnet run --project Tests/BridgeCheck.csproj -p:AceBinDir=<ACE build folder>` from this directory. The harness runs the bridge on loopback and checks ticket replacement, one-time redemption, and concurrent requests.

For an end-to-end check, pass an empty scratch folder to the harness after `--`. Set the racer's `APPDATA` to that folder's `appdata` subfolder and launch Godot with `--headless --path <GromnieGrandPrix folder> -- --smoke-test --ace-rider-check=<scratch folder>`. The test imports a rider from the bridge, reloads its saved appearance, and starts a race with it.
