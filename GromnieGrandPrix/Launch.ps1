param([string]$DatFolder, [string]$Godot, [switch]$Editor, [switch]$SmokeTest, [string]$Capture)
$ErrorActionPreference = 'Stop'
if (-not $Godot) { $Godot = $env:GROMNIE_GODOT }
if (-not $Godot) {
    $Godot = (Get-Command 'Godot_v4.7.2-stable_mono_win64_console.exe' -ErrorAction SilentlyContinue).Source
}
if (-not $Godot) {
    $codex = Split-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot)))
    $installed = Join-Path $codex '2026-11-14/can-we-make-a-web-based/work/godot'
    $Godot = (Get-ChildItem -LiteralPath $installed -Filter '*mono_win64_console.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}
if (-not $Godot -or -not (Test-Path -LiteralPath $Godot)) {
    throw 'Set GROMNIE_GODOT or pass -Godot with the path to the Godot 4.7.2 .NET console executable.'
}
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The .NET 8 SDK is required.' }
$env:DOTNET_CLI_HOME = Join-Path ([IO.Path]::GetTempPath()) 'gromnie-dotnet'
$env:NUGET_PACKAGES = Join-Path $env:DOTNET_CLI_HOME 'packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PATH = (Split-Path $dotnet) + ';' + $env:PATH
$feed = Join-Path (Split-Path $Godot) 'GodotSharp/Tools/nupkgs'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$nuget = '<configuration><packageSources><clear/><add key="godot-local" value="' + $escapedFeed + '"/></packageSources><packageSourceMapping><clear/><packageSource key="godot-local"><package pattern="*"/></packageSource></packageSourceMapping></configuration>'
Set-Content -LiteralPath $config -Value $nuget
& $dotnet restore (Join-Path $PSScriptRoot 'GromnieGrandPrix.csproj') --verbosity quiet
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $PSScriptRoot 'GromnieGrandPrix.csproj') --no-restore --verbosity quiet --nologo
if ($LASTEXITCODE) { exit $LASTEXITCODE }
if (-not $Editor -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.godot/imported'))) {
    & $Godot --headless --path $PSScriptRoot --editor --import --log-file (Join-Path $env:DOTNET_CLI_HOME 'import.log')
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}
$argsForGodot = @('--path', $PSScriptRoot, '--log-file', (Join-Path $env:DOTNET_CLI_HOME 'game.log'))
if ($Editor) { $argsForGodot += '--editor' }
if ($SmokeTest) { $argsForGodot += '--headless' }
$argsForGodot += '--'
if ($DatFolder) { $argsForGodot += "--dat=$DatFolder" }
if ($SmokeTest) { $argsForGodot += '--smoke-test' }
if ($Capture) { $argsForGodot += "--capture=$Capture" }
& $Godot @argsForGodot
exit $LASTEXITCODE




