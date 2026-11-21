[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 8040,

    [string]$RelayPath,

    [switch]$Check,

    [string]$CheckUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $publicBase = [uri]'https://ggprix.derptide.net'
    $settingsFile = Join-Path $PSScriptRoot 'GGPrix-Server.json'
    if (Test-Path -LiteralPath $settingsFile -PathType Leaf) {
        $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        if (-not $PSBoundParameters.ContainsKey('Port')) {
            if (-not [int]::TryParse([string]$settings.Port, [ref]$Port) -or $Port -lt 1 -or $Port -gt 65535) {
                throw 'Set Port to an integer between 1 and 65535 in GGPrix-Server.json.'
            }
        }
        $publicBase = [uri]$settings.PublicUrl
        if (-not $publicBase.IsAbsoluteUri -or $publicBase.Scheme -ne 'https' -or
            $publicBase.AbsolutePath -ne '/' -or $publicBase.Query -or $publicBase.Fragment -or $publicBase.UserInfo) {
            throw 'Set PublicUrl to the base HTTPS URL in GGPrix-Server.json.'
        }
    }

    $privateDotnet = Join-Path $PSScriptRoot 'runtime/dotnet.exe'
    if (-not $RelayPath) {
        $RelayPath = Join-Path $PSScriptRoot 'RaceRelay.dll'
        if (-not (Test-Path -LiteralPath $privateDotnet -PathType Leaf)) {
            $existingExe = Join-Path $PSScriptRoot 'RaceRelay.exe'
            if (Test-Path -LiteralPath $existingExe -PathType Leaf) { $RelayPath = $existingExe }
        }
        if (-not (Test-Path -LiteralPath $RelayPath -PathType Leaf)) {
            throw 'Extract the entire relay ZIP into a folder before starting it.'
        }
    }

    if (-not (Test-Path -LiteralPath $RelayPath -PathType Leaf)) {
        throw "Relay file was not found at '$RelayPath'."
    }
    $relay = Get-Item -LiteralPath $RelayPath
    if ($relay.Extension -notin '.exe', '.dll') {
        throw 'Supply a published RaceRelay.exe or RaceRelay.dll.'
    }

    $listenUrl = "http://127.0.0.1:$Port"
    $serverCommand = $relay.FullName
    $serverArguments = if ($Check) {
        if (-not $CheckUrl) { $CheckUrl = $publicBase.AbsoluteUri }
        @('--check', $CheckUrl)
    } else {
        @('--urls', $listenUrl)
    }
    if ($relay.Extension -eq '.dll') {
        if (Test-Path -LiteralPath $privateDotnet -PathType Leaf) {
            $serverCommand = $privateDotnet
        } else {
            $dotnet = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $dotnet) { throw 'Extract the entire relay ZIP, including its runtime directory.' }
            $serverCommand = $dotnet.Source
        }
        $serverArguments = @($relay.FullName) + $serverArguments
    }

    Push-Location -LiteralPath $relay.DirectoryName
    try {
        if (-not $Check) {
            $publicSocket = [UriBuilder]::new($publicBase)
            $publicSocket.Scheme = 'wss'
            $publicSocket.Path = '/race'
            Write-Host 'Gromnie Grand Prix Server'
            Write-Host "Local relay: ws://127.0.0.1:$Port/race"
            Write-Host "Health check: $listenUrl/health"
            Write-Host "Public relay after hostname setup: $($publicSocket.Uri)"
            Write-Host 'Press Ctrl+C to stop the relay.'
        }
        & $serverCommand @serverArguments
        $relayExit = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    exit $relayExit
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
