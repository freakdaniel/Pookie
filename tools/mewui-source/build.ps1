# SPDX-License-Identifier: BSD-3-Clause
# Build the pinned upstream package family, including Core's compiler/preview assets.
$ErrorActionPreference = 'Stop'
$revision = Get-Content (Join-Path $PSScriptRoot 'revision.json') -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$checkout = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Pookie.Build/MewUI/$($revision.commit)"
$feed = Join-Path $root 'artifacts/mewui-source/packages'
function Invoke-Checked([string] $program, [string[]] $arguments) {
    & $program @arguments
    if ($LASTEXITCODE -ne 0) { throw "$program failed with exit code $LASTEXITCODE" }
}
New-Item -ItemType Directory -Force $feed | Out-Null
New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($checkout)) | Out-Null
if (!(Test-Path (Join-Path $checkout '.git'))) {
    Invoke-Checked git @('clone', '--filter=blob:none', '--no-checkout', $revision.repository, $checkout)
    Invoke-Checked git @('-C', $checkout, 'checkout', '--detach', $revision.commit)
}
$remote = & git -C $checkout remote get-url origin
if ($LASTEXITCODE -ne 0 -or $remote -ne $revision.repository) { throw 'Unexpected MewUI checkout remote.' }
$changes = & git -C $checkout status --porcelain
if ($LASTEXITCODE -ne 0 -or $changes) { throw 'MewUI source checkout has local changes; preserve or move them before rebuilding.' }
& git -C $checkout cat-file -e "$($revision.commit)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { Invoke-Checked git @('-C', $checkout, 'fetch', 'origin', $revision.commit) }
Invoke-Checked git @('-C', $checkout, 'checkout', '--detach', $revision.commit)
# Do not inherit Pookie's Directory.Build files from the checkout's ancestors.
$common = @('-c', 'Release', '-p:ImportDirectoryBuildProps=false', '-p:ImportDirectoryBuildTargets=false',
    "-p:PackageVersion=$($revision.packageVersion)", '-p:ContinuousIntegrationBuild=true', '--disable-build-servers', '-m:1', '--verbosity', 'quiet')
foreach ($name in @('MewUI.Analyzers', 'MewUI.Generators', 'MewUI.Preview')) {
    Invoke-Checked dotnet (@('build', (Join-Path $checkout "src/$name/$name.csproj")) + $common)
}
foreach ($name in @('MewUI', 'MewUI.Platform.Win32', 'MewUI.Platform.X11', 'MewUI.Platform.MacOS',
    'MewUI.Backend.Direct2D', 'MewUI.Backend.Gdi', 'MewUI.Backend.MewVG.Win32', 'MewUI.Backend.MewVG.X11', 'MewUI.Backend.MewVG.MacOS')) {
    Invoke-Checked dotnet (@('pack', (Join-Path $checkout "src/$name/$name.csproj"), '-o', $feed) + $common)
}
Invoke-Checked dotnet (@('pack', (Join-Path $checkout 'meta/MewUI.All/MewUI.All.csproj'), '-o', $feed) + $common)
Write-Host "MewUI $($revision.commit) packages are ready in $feed"
