param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][ValidateSet('dependencies', 'ui')][string]$Check,
    [string]$VideoFixture,
    [Parameter(Mandatory)][string]$LogFile
)
$ErrorActionPreference = 'Stop'
$arguments = @('--aot-ui-check')
$markers = @('AOT_BROWSER_OK:', 'AOT_UI_OK:')
if ($Check -eq 'dependencies') {
    if (-not $VideoFixture) { throw 'The dependency check requires a video fixture.' }
    $arguments = @('--aot-self-test', $VideoFixture)
    $markers = @('AOT_LOGGING_OK:', 'AOT_METADATA_OK:', 'AOT_AUDIO_OK:', 'AOT_VIDEO_OK:', 'NATIVE_AOT_OK:')
}
$lines = @(& $Executable @arguments 2>&1 | Tee-Object -FilePath $LogFile)
if ($LASTEXITCODE -ne 0) { throw "Native $Check check exited with code $LASTEXITCODE. See $LogFile." }
foreach ($marker in $markers) {
    if (-not ($lines | Where-Object { $_.ToString().StartsWith($marker, [StringComparison]::Ordinal) })) {
        throw "Native $Check check is missing $marker See $LogFile."
    }
}
$lines
