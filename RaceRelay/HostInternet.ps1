param(
    [string]$Cloudflared,
    [int]$Port = 8040,
    [string]$Godot,
    [string]$DatFolder
)
$ErrorActionPreference = 'Stop'
if (-not $Cloudflared) {
    $localCloudflared = Join-Path $PSScriptRoot 'tools/cloudflared.exe'
    if (Test-Path -LiteralPath $localCloudflared -PathType Leaf) { $Cloudflared = $localCloudflared }
    else { $Cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue).Source }
}
if (-not $Cloudflared -or -not (Test-Path -LiteralPath $Cloudflared -PathType Leaf)) {
    throw 'Install cloudflared or pass its executable as -Cloudflared. See RaceRelay/README.md.'
}
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose a port between 1024 and 65535.' }
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
& $dotnet build (Join-Path $PSScriptRoot 'RaceRelay.csproj') -c Release --nologo --verbosity quiet
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$local = "http://127.0.0.1:$Port"
$log = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ('gromnie-relay-' + [Guid]::NewGuid().ToString('N')))).FullName
$relayPath = Join-Path $PSScriptRoot 'bin/Release/net8.0/RaceRelay.dll'
$relay = $null
$tunnel = $null
$priorEndpoint = $env:GROMNIE_RELAY
$priorInvite = $env:GROMNIE_INVITE_RELAY
try {
    $relay = Start-Process -FilePath $dotnet -ArgumentList @(('"{0}"' -f $relayPath), '--urls', $local) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $log 'relay.log') -RedirectStandardError (Join-Path $log 'relay-error.log')
    $ready = $false
    for ($i = 0; $i -lt 24 -and !$relay.HasExited; $i++) {
        try { $ready = (Invoke-RestMethod -Uri "$local/health" -TimeoutSec 2).ready } catch { Start-Sleep -Milliseconds 250 }
        if ($ready) { break }
    }
    if (!$ready -or $relay.HasExited) { throw "The relay could not start on port $Port. Logs are in $log" }
    $tunnelLog = Join-Path $log 'tunnel.log'
    $tunnel = Start-Process -FilePath $Cloudflared -ArgumentList @('tunnel','--no-autoupdate','--protocol','http2','--url',$local) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $log 'tunnel-output.log') -RedirectStandardError $tunnelLog
    $endpoint = $null
    for ($i = 0; $i -lt 44 -and !$tunnel.HasExited; $i++) {
        $text = Get-Content -LiteralPath $tunnelLog -Raw -ErrorAction SilentlyContinue
        if ($text -match '(https://[a-z0-9-]+\.trycloudflare\.com)') {
            $endpoint = $Matches[1]
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (!$endpoint) { throw "Cloudflare did not return a relay address. Logs are in $log" }
    $ready = $false
    $publicName = ([Uri]$endpoint).Host
    $curl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
    $publicIp = $null
    for ($i = 0; $i -lt 24 -and !$tunnel.HasExited; $i++) {
        try {
            if (!$publicIp) {
                $ready = (Invoke-RestMethod -Uri "$endpoint/health" -TimeoutSec 2).ready
            }
        } catch {
            if ($curl) {
                try {
                    $dns = Invoke-RestMethod -Uri "https://cloudflare-dns.com/dns-query?name=$publicName&type=A" -Headers @{ Accept = 'application/dns-json' } -TimeoutSec 3
                    $publicIp = ($dns.Answer | Where-Object { $_.type -eq 1 } | Select-Object -First 1).data
                } catch { }
            }
        }
        if (!$ready -and $publicIp) {
            $json = & $curl --silent --fail --max-time 3 --resolve "${publicName}:443:$publicIp" "$endpoint/health"
            if (!$LASTEXITCODE -and $json) { $ready = ($json | ConvertFrom-Json).ready }
        }
        if ($ready) { break }
        Start-Sleep -Milliseconds 650
    }
    if (!$ready) { throw "The internet relay could not be reached. Logs are in $log" }
    $env:GROMNIE_RELAY = "ws://127.0.0.1:$Port/race"
    $env:GROMNIE_INVITE_RELAY = "wss://$publicName/race"
    $play = Join-Path $PSScriptRoot '../GromnieGrandPrix/Play.cmd'
    $arguments = @()
    if ($Godot) { $arguments += @('-Godot', $Godot) }
    if ($DatFolder) { $arguments += @('-DatFolder', $DatFolder) }
    & $play @arguments
    $gameExit = $LASTEXITCODE
} finally {
    $env:GROMNIE_RELAY = $priorEndpoint
    $env:GROMNIE_INVITE_RELAY = $priorInvite
    foreach ($process in @($tunnel,$relay)) {
        if ($null -eq $process) { continue }
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id }
    }
}
exit $gameExit
