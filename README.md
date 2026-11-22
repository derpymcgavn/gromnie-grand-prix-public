# Gromnie Grand Prix

An arcade racer built in Godot. Ride a gromnie through Dereth, collect spell pickups, and build custom circuits on native landblocks.

## Windows download

Download and run `Gromnie-Grand-Prix-Setup-x64.exe` from the [Windows v1.0.9 release](https://github.com/derpymcgavn/gromnie-grand-prix-public/releases/tag/v1.0.9). On first launch, choose the folder containing `client_portal.dat` and `client_cell_1.dat` from your Asheron's Call installation.

## Run from source

Use Godot 4.7.2 .NET, the .NET 8 SDK, and your installed Asheron’s Call client DATs. The game reads `client_portal.dat` and `client_cell_1.dat` from the installation folder.

From PowerShell in the repository root, point the launcher at your Godot .NET console executable and DAT folder.

```powershell
.\GromnieGrandPrix\Play.cmd -Godot 'C:\Tools\Godot\Godot_v4.7.2-stable_mono_win64_console.exe' -DatFolder "C:\Turbine\Asheron's Call"
```

Set `GROMNIE_GODOT` to your Godot executable to use the launcher without `-Godot`. The stable can also prompt for the DAT folder.

See the [game guide](GromnieGrandPrix/Readme.txt) for controls, courses, and the track editor. In the stable, **Import from ACE → Server** connects directly to an ACE shard and saves the selected character’s appearance as a local template. The [ACE rider bridge](AceRiderBridge/README.md) also supports imports by pairing code.

Open **Play Online** to host or join a race by invite code. Use lobby chat before the host starts the countdown. See [Race Relay setup](RaceRelay/README.md) for the shared service used by online rooms.

To export a Windows x64 build, install the matching Godot .NET export templates and use the **Windows Desktop** export preset. The compiled game uses each player's installed Asheron's Call DATs. The separate soundtrack contains twelve original tracks by Derpy.

Use the [Windows installer recipe](deploy/windows/README.md) to package the exported game and soundtrack with the DAT setup guide and uninstaller.

## Android

Download the APK from the [Android v1.0.11 release](https://github.com/derpymcgavn/gromnie-grand-prix-public/releases/tag/v1.0.11). The Android ARM64 build imports both DAT files from a folder selected in Android's file picker. See the [Android build and setup instructions](GromnieGrandPrix/Android/README.md).

## Verify a local build

Run the same command with `-SmokeTest` to build the project and run the headless game checks.

See the [third-party notices](GromnieGrandPrix/THIRD_PARTY_NOTICES.txt) for the OpenAC terrain code notice and the [Godot Engine license](https://godotengine.org/license) for the engine and its dependencies.
