# Build the Windows installer

Compile with [Inno Setup 6](https://jrsoftware.org/inno-setup/downloads) after assembling the Windows x64 game folder. That folder contains `GromnieGrandPrix.exe`, `GromnieGrandPrix.pck`, its bundled .NET runtime directory, `README.txt`, `ThirdPartyNotices`, and `Soundtrack` with Derpy’s original MP3s and readme.

```powershell
& $iscc "/DReleaseDir=$releaseFolder" "/DReleaseVersion=0.1.0" "/O$destinationFolder" .\deploy\windows\Install-GGPrix.iss
```

Set `$iscc` to the path to `ISCC.exe` and use absolute paths for the release and output folders. Use the same application ID for future releases.

The installer creates a per-user Windows Apps entry, Start menu shortcuts, and an optional desktop shortcut. Players can choose their own Asheron’s Call DAT folder on launch. The game’s saves, settings, rider templates, and tracks live in the Windows user profile.
