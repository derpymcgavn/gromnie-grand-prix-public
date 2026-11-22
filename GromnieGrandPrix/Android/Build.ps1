# Run with PowerShell 7. This build uses a portable Godot .NET editor with an _sc_ marker.
param(
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$Godot,
    [Parameter(Mandatory)][string]$TemplateDirectory,
    [Parameter(Mandatory)][string]$AndroidSdk,
    [Parameter(Mandatory)][string]$JavaHome,
    [Parameter(Mandatory)][string]$Apk,
    [string]$DatFolder = "C:/Turbine/Asheron's Call",
    [switch]$FullDats,
    [switch]$ImportDats,
    [string]$SigningKeystore,
    [string]$SigningAlias = "gromnie-grand-prix"
)

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot
if ($ImportDats) {
    if ($FullDats) { throw '-FullDats applies to bundled builds only.' }
    if (-not $SigningKeystore -or -not $env:GROMNIE_SIGNING_PASSWORD) {
        throw 'Supply -SigningKeystore and the GROMNIE_SIGNING_PASSWORD environment variable for a public build.'
    }
    $SigningKeystore = (Resolve-Path -LiteralPath $SigningKeystore).Path
}
else {
    $DatFolder = (Resolve-Path -LiteralPath $DatFolder).Path
}
$Godot = (Resolve-Path -LiteralPath $Godot).Path
$godotDirectory = Split-Path $Godot
if (-not (Test-Path -LiteralPath (Join-Path $godotDirectory '_sc_'))) {
    throw 'Use a separate portable Godot editor with an _sc_ marker next to the executable.'
}
$templates = (Resolve-Path -LiteralPath $TemplateDirectory).Path.Replace('\','/')
$AndroidSdk = (Resolve-Path -LiteralPath $AndroidSdk).Path.Replace('\','/')
$JavaHome = (Resolve-Path -LiteralPath $JavaHome).Path.Replace('\','/')
$work = (New-Item -ItemType Directory -Path $WorkDirectory -Force).FullName
$stageRoot = Join-Path $work $(if ($ImportDats) { 'stage-import' } else { 'stage' })
$stage = Join-Path $stageRoot 'GromnieGrandPrix'
$relay = Join-Path $stageRoot 'RaceRelay'
$Apk = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Apk)
$dotnet = (Get-Command dotnet).Source
$buildTools = Join-Path $AndroidSdk 'build-tools/35.0.1'
$dotnetSdk = & $dotnet --list-sdks | Where-Object { $_ -match '^9\.0\.' } | Select-Object -Last 1
if (-not $dotnetSdk) { throw '.NET SDK 9.0 is required for this Android template.' }

New-Item -ItemType Directory -Force -Path $stage,$relay | Out-Null
& robocopy $project $stage /E /XD .godot .git Android BundledDats /XF NuGet.Config export_presets.cfg /R:0 /W:0 /NFL /NDL /NJH /NJS /NC /NS /NP
if ($LASTEXITCODE -ge 8) { throw "Project staging failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath "$project/../RaceRelay/Wire.cs" -Destination "$relay/Wire.cs"
@{ sdk = @{ version = $dotnetSdk.Split(' ')[0] } } | ConvertTo-Json -Depth 2 | Set-Content "$stageRoot/global.json"

$env:DOTNET_CLI_HOME = Join-Path $work 'dotnet'
$env:NUGET_PACKAGES = Join-Path $env:DOTNET_CLI_HOME 'packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:JAVA_HOME = $JavaHome
$env:PATH = (Split-Path $dotnet) + ';' + (Join-Path $JavaHome 'bin') + ';' + $env:PATH
$env:ANDROID_HOME = $AndroidSdk
$env:ANDROID_USER_HOME = Join-Path $work 'toolchain/android-user'
$env:ADB_VENDOR_KEYS = Join-Path $env:ANDROID_USER_HOME 'adbkey'
$env:APPDATA = Join-Path $work 'editor-appdata'
$env:TEMP = Join-Path $work 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:ANDROID_USER_HOME,$env:APPDATA,$env:TEMP | Out-Null

$feed = [Security.SecurityElement]::Escape((Join-Path $godotDirectory 'GodotSharp/Tools/nupkgs'))
@"
<configuration>
  <packageSources><clear/><add key="godot-local" value="$feed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping><clear/><packageSource key="godot-local"><package pattern="Godot*"/></packageSource><packageSource key="nuget.org"><package pattern="Microsoft.*"/></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content "$stage/NuGet.Config"

$editorData = Join-Path $godotDirectory 'editor_data'
New-Item -ItemType Directory -Force $editorData | Out-Null
@"
[gd_resource type="EditorSettings" format=3]

[resource]
export/android/android_sdk_path = "$AndroidSdk"
export/android/java_sdk_path = "$JavaHome"
"@ | Set-Content "$editorData/editor_settings-4.7.tres"

$config = Get-Content -Raw "$stage/project.godot"
$config = $config.Replace('[rendering]', "[rendering]`ntextures/vram_compression/import_etc2_astc=true")
$config = $config.Replace('[display]', "[display]`nwindow/handheld/orientation=0")
$config += "`n[input_devices]`npointing/emulate_mouse_from_touch=true`n"
Set-Content -LiteralPath "$stage/project.godot" $config

$packageName = if ($ImportDats) { 'com.gromnie.grandprix' } else { 'com.gromnie.grandprix.privatebuild' }
$versionCode = if ($ImportDats) { 12 } else { 9 }
$versionName = if ($ImportDats) { '1.0.11' } else { '1.0.8' }
$datAssets = if ($ImportDats) { '' } else { ',BundledDats/*.bin,BundledDats/manifest.json' }
$excludedBundle = if ($ImportDats) { ',BundledDats/*' } else { '' }

@"
[preset.0]

name="Android ARM64"
platform="Android"
runnable=true
advanced_options=true
export_filter="all_resources"
include_filter="Data/*.json,RelayUrl.txt$datAssets"
exclude_filter="NuGet.Config,*.ps1,*.cmd,*.csproj,*.sln,*.dat,*.keystore,*.jks$excludedBundle"

[preset.0.options]

custom_template/debug="$templates/android_debug.apk"
custom_template/release="$templates/android_release.apk"
gradle_build/use_gradle_build=false
architectures/armeabi-v7a=false
architectures/arm64-v8a=true
architectures/x86=false
architectures/x86_64=false
package/unique_name="$packageName"
version/code=$versionCode
version/name="$versionName"
package/signed=false
permissions/internet=true
dotnet/include_debug_symbols=false
"@ | Set-Content "$stage/export_presets.cfg"

if (-not $ImportDats -and -not $FullDats) {
    $packer = Join-Path $work 'stage/DatPacker'
    New-Item -ItemType Directory -Force $packer | Out-Null
    Copy-Item -LiteralPath "$PSScriptRoot/DatPacker/DatPacker.csproj","$PSScriptRoot/DatPacker/Program.cs" -Destination $packer
    $compactDats = Join-Path $work 'compact-dats'
    Push-Location $packer
    try {
        & $dotnet run --project DatPacker.csproj --configuration Release "-p:GameProjectDirectory=$stage" -p:NuGetAudit=false -- $DatFolder $compactDats $stage
        if ($LASTEXITCODE) { throw "Compact DAT generation failed ($LASTEXITCODE)." }
    }
    finally { Pop-Location }
    $DatFolder = $compactDats
}

if (-not $ImportDats) {
    $bundle = (New-Item -ItemType Directory -Force (Join-Path $stage 'BundledDats')).FullName
    $manifest = [ordered]@{}
    $buffer = [byte[]]::new(16MB)
    foreach ($name in 'client_portal.dat','client_cell_1.dat') {
        Get-ChildItem -LiteralPath $bundle -Filter "$name.*.bin" -File | Remove-Item
        $datStream = [IO.File]::OpenRead((Join-Path $DatFolder $name))
        $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
        try {
            $part = 0
            while ($datStream.Position -lt $datStream.Length) {
                $count = [int][Math]::Min($buffer.Length, $datStream.Length - $datStream.Position)
                $datStream.ReadExactly($buffer, 0, $count)
                $file = Join-Path $bundle ('{0}.{1:D3}.bin' -f $name,$part)
                $part++
                $output = [IO.File]::Create($file)
                try { $output.Write($buffer, 0, $count) } finally { $output.Dispose() }
                $hash.AppendData($buffer, 0, $count)
            }
            $manifest[$name] = @{ Bytes = $datStream.Length; Sha256 = [Convert]::ToHexString($hash.GetHashAndReset()) }
            Write-Host "Staged $name ($($datStream.Length) bytes, $part parts)."
        }
        finally {
            $datStream.Dispose()
            $hash.Dispose()
        }
    }
    $manifest | ConvertTo-Json | Set-Content "$bundle/manifest.json"
}

Push-Location $stage
try {
    if (-not (Test-Path -LiteralPath 'GromnieGrandPrix.sln')) {
        & $dotnet new sln --name GromnieGrandPrix
        if ($LASTEXITCODE) { throw 'Could not generate the staged solution.' }
        & $dotnet sln GromnieGrandPrix.sln add GromnieGrandPrix.csproj
        if ($LASTEXITCODE) { throw 'Could not add the staged project to the solution.' }
    }
    & $dotnet build GromnieGrandPrix.csproj --configuration Debug --verbosity quiet
    if ($LASTEXITCODE) { throw "Desktop editor build failed ($LASTEXITCODE)." }
    & $Godot --headless --editor --import --log-file "$work/import.log" *> "$work/import-console.log"
    if ($LASTEXITCODE) { throw "Godot import failed (see $work/import-console.log)." }

    $unsigned = Join-Path $work 'gromnie-unsigned.apk'
    $aligned = Join-Path $work 'gromnie-aligned.apk'
    & $Godot --headless --export-release 'Android ARM64' $unsigned --log-file "$work/export.log" *> "$work/export-console.log"
    # The exporter falls back to its built-in icon and may log an editor teardown diagnostic.
    $exportErrors = Select-String -LiteralPath "$work/export-console.log" -Pattern '^ERROR:' |
        Where-Object { $_.Line -notmatch '^ERROR: (No project icon specified\.|EditorSettings not instantiated yet when getting setting "export/android/(android_sdk_path|shutdown_adb_on_exit)")' }
    if ($LASTEXITCODE -or -not (Test-Path -LiteralPath $unsigned) -or $exportErrors) {
        throw "Godot could not export ARM64 (see $work/export-console.log)."
    }
    $package = [IO.Compression.ZipFile]::OpenRead($unsigned)
    try {
        $requiredEntries = @('assets/.godot/mono/publish/arm64/GromnieGrandPrix.dll')
        if (-not $ImportDats) { $requiredEntries += 'assets/BundledDats/manifest.json' }
        foreach ($entry in $requiredEntries) {
            if (-not $package.GetEntry($entry)) { throw "Godot omitted $entry from the APK." }
        }
        if ($ImportDats -and ($package.Entries.FullName -match '(?i)(^|/)BundledDats/|\.dat$')) {
            throw 'The public APK unexpectedly contains bundled DAT data.'
        }
    }
    finally { $package.Dispose() }

    $key = Join-Path $work 'gromnie-debug.keystore'
    $keyPassword = 'pass:android'
    $keyAlias = 'androiddebugkey'
    if ($ImportDats) {
        $key = $SigningKeystore
        $keyPassword = 'env:GROMNIE_SIGNING_PASSWORD'
        $keyAlias = $SigningAlias
    }
    elseif (-not (Test-Path -LiteralPath $key)) {
        & "$JavaHome/bin/keytool.exe" -genkeypair -keystore $key -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Gromnie Grand Prix Local Debug'
        if ($LASTEXITCODE) { throw 'Could not create the local debug key.' }
    }
    & "$buildTools/zipalign.exe" -f -P 16 4 $unsigned $aligned
    if ($LASTEXITCODE) { throw 'APK alignment failed.' }
    $signed = Join-Path $work 'gromnie-signed.apk'
    & "$JavaHome/bin/java.exe" -jar "$buildTools/lib/apksigner.jar" sign --ks $key --ks-pass $keyPassword --ks-key-alias $keyAlias --v4-signing-enabled false --out $signed $aligned
    if ($LASTEXITCODE) { throw 'APK signing failed.' }
    & "$JavaHome/bin/java.exe" -jar "$buildTools/lib/apksigner.jar" verify --verbose $signed
    if ($LASTEXITCODE) { throw 'APK signature verification failed.' }
    & "$buildTools/zipalign.exe" -c -P 16 4 $signed
    if ($LASTEXITCODE) { throw 'Signed APK alignment verification failed.' }
    Move-Item -Force -LiteralPath $signed -Destination $Apk
    Get-Item -LiteralPath $Apk | Select-Object FullName,Length
}
finally { Pop-Location }
