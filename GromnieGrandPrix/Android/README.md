# Android ARM64

The Android build uses Godot 4.7.2 .NET and its matching Android export templates, the .NET 9 SDK, JDK 17, and the Android SDK with Build Tools 35.0.1. Use PowerShell 7 and a separate Godot editor with an `_sc_` marker beside its executable.

Run [Build.ps1](Build.ps1) with `-ImportDats` to build an APK that reads each player's own Asheron's Call assets. Supply a release keystore via `-SigningKeystore` and set `GROMNIE_SIGNING_PASSWORD` in the build process. The default signing alias is `gromnie-grand-prix`. Keep the key and password outside the repository and retain them for future updates.

```powershell
./GromnieGrandPrix/Android/Build.ps1 `
    -WorkDirectory C:/Builds/GromnieAndroid `
    -Godot C:/Tools/Godot/Godot_v4.7.2-stable_mono_win64_console.exe `
    -TemplateDirectory C:/Tools/Godot/templates `
    -AndroidSdk C:/Tools/Android/sdk `
    -JavaHome C:/Tools/jdk-17 `
    -Apk C:/Builds/GromnieGrandPrix-Android-ARM64.apk `
    -ImportDats -SigningKeystore C:/Signing/gromnie-grand-prix.p12
```

## First launch

1. Copy `client_portal.dat` and `client_cell_1.dat` from your Asheron's Call installation into a folder on the Android device. A subfolder such as `Download/GromnieData` works.
2. Open Gromnie Grand Prix, tap **Choose Folder**, select that folder, and tap **Use this folder** in Android's file picker.
3. The game makes an app-private copy for future launches. Allow about 1.2 GiB of additional free space for the original retail files.

The release package ID is `com.gromnie.grandprix`. It installs alongside older private test builds.

## Racing controls

Push the joystick up to accelerate, down to brake, and sideways to steer. Hold **Drift** while steering to charge a boost. In Free Flight, the joystick banks left or right, dives when pushed up, and climbs when pulled down.

Open **Pause → Touch Layout** to drag controls, resize them, or swap the driving controls to the other side. **Edit Flight** shows the airborne layout, including Air Roll. **Done** saves the layout for future races.
