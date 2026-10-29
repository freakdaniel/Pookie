param(
    [string]$ResearchFile = (Join-Path $PSScriptRoot '..\docs\soundcloud-api-internals.md'),
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Pookie\Config\mobile-oauth.json')
)
$ErrorActionPreference = 'Stop'
# Local research credentials remain outside the checkout and are never printed.
if (Test-Path -LiteralPath $Destination) {
    Write-Output 'Mobile OAuth configuration already exists; preserving its credentials and stable identity.'
    exit 0
}
$research = Get-Content -LiteralPath $ResearchFile -Raw
function Read-Credential([string]$name, [string]$kind) {
    $pattern = '\| `' + [regex]::Escape($name) + '` \(' + $kind + '\) \| `([A-Za-z0-9]+)`'
    $match = [regex]::Match($research, $pattern)
    if (-not $match.Success) { throw "Research is missing the $name $kind entry." }
    return $match.Groups[1].Value
}
$profile = @{
    client_id = Read-Credential 'client_id' 'активный'
    client_secret = Read-Credential 'client_secret' 'активный'
    app_id = 3152
    exchange_client = @{
        client_id = Read-Credential 'client_id' 'legacy'
        client_secret = Read-Credential 'client_secret' 'legacy'
    }
    mobile_identity = @{
        # An installation profile, not measurements from a physical Android.
        android_id = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(8)).ToLowerInvariant()
        manufacturer = 'Pookie'
        model = 'Desktop'
        android_version = '15'
        app_build = 377050
        app_version = '2026.10.14-release'
    }
}
$parent = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Destination))
[IO.Directory]::CreateDirectory($parent) | Out-Null
$json = $profile | ConvertTo-Json -Depth 6
# CreateNew protects an existing configuration even if another process races us.
$stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    try { $writer.Write($json) } finally { $writer.Dispose() }
} finally { $stream.Dispose() }
Write-Output 'Mobile profile enabled for normal startup. No hardware/sensor telemetry is fabricated. Set POOKIE_SOUNDCLOUD_PROFILE=web to use the existing web profile.'
