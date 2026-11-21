[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows) -or
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne
        [System.Runtime.InteropServices.Architecture]::X64) {
    throw 'Run this publisher on Windows x64.'
}

$dotnet = (Get-Command dotnet.exe -CommandType Application | Select-Object -First 1).Source
$dotnetHome = Split-Path -Parent $dotnet
$coreHome = Join-Path $dotnetHome 'shared/Microsoft.NETCore.App'
$aspHome = Join-Path $dotnetHome 'shared/Microsoft.AspNetCore.App'
$runtimeVersion = Get-ChildItem -LiteralPath $coreHome -Directory |
    Where-Object {
        $_.Name -match '^8\.0\.\d+$' -and
        (Test-Path -LiteralPath (Join-Path $aspHome $_.Name) -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $dotnetHome "host/fxr/$($_.Name)/hostfxr.dll") -PathType Leaf)
    } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty Name
if (-not $runtimeVersion) {
    throw 'Install matching .NET 8 and ASP.NET Core 8 Windows x64 runtimes on the build machine.'
}

$destinationDirectory = (New-Item -ItemType Directory -Path $Destination -Force).FullName
$releaseName = 'GGPrix-Relay-win-x64'
$archive = Join-Path $destinationDirectory "$releaseName.zip"
$stagingName = "$releaseName-build-$([Guid]::NewGuid().ToString('N'))"
$release = (New-Item -ItemType Directory -Path (Join-Path $destinationDirectory $stagingName)).FullName

try {
    $project = Join-Path $PSScriptRoot '../RaceRelay/RaceRelay.csproj'
    & $dotnet publish $project -c Release --no-self-contained --nologo -p:UseAppHost=false -p:DebugType=none -p:DebugSymbols=false -o $release
    if ($LASTEXITCODE -ne 0) { throw 'RaceRelay publish failed.' }

    # Carry a private copy of Microsoft’s hosting runtime so the server box needs only the ZIP.
    $runtime = Join-Path $release 'runtime'
    foreach ($folder in @('host/fxr', 'shared/Microsoft.NETCore.App', 'shared/Microsoft.AspNetCore.App')) {
        $target = (New-Item -Path (Join-Path $runtime $folder) -ItemType Directory -Force).FullName
        Copy-Item -LiteralPath (Join-Path $dotnetHome "$folder/$runtimeVersion") -Destination $target -Recurse
    }
    $runtimeFiles = @('dotnet.exe', 'LICENSE.txt', 'ThirdPartyNotices.txt') |
        ForEach-Object { Join-Path $dotnetHome $_ }
    Copy-Item -LiteralPath $runtimeFiles -Destination $runtime
    $reportedRuntimes = & (Join-Path $runtime 'dotnet.exe') --list-runtimes
    if ($LASTEXITCODE -ne 0 -or
        -not ($reportedRuntimes -like "Microsoft.NETCore.App $runtimeVersion *") -or
        -not ($reportedRuntimes -like "Microsoft.AspNetCore.App $runtimeVersion *")) {
        throw 'The bundled runtime failed to start.'
    }
    Write-Host "Bundled .NET and ASP.NET Core $runtimeVersion."

    $windows = Join-Path $PSScriptRoot 'ggprix-windows'
    $windowsFiles = @(
        'Start-GGPrix-Server.cmd', 'Start-GGPrix-Server.ps1', 'Check-GGPrix-Connection.cmd',
        'GGPrix-Server.json', 'README.md'
    )
    Copy-Item -LiteralPath ($windowsFiles | ForEach-Object { Join-Path $windows $_ }) -Destination $release
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ggprix/README.md') -Destination (Join-Path $release 'DERPTIDE-HOSTNAME.md')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ggprix/Caddyfile') -Destination $release

    $releaseFiles = $windowsFiles + @(
        'RaceRelay.dll', 'RaceRelay.deps.json', 'RaceRelay.runtimeconfig.json',
        'DERPTIDE-HOSTNAME.md', 'Caddyfile', 'runtime'
    ) | ForEach-Object { Join-Path $release $_ }
    Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -Force
}
finally {
    $resolvedStaging = (Resolve-Path -LiteralPath $release).ProviderPath
    $expectedStaging = Join-Path (Resolve-Path -LiteralPath $destinationDirectory).ProviderPath $stagingName
    if ($resolvedStaging -eq $expectedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}
Get-Item -LiteralPath $archive
