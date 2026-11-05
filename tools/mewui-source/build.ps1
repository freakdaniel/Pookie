# SPDX-License-Identifier: BSD-3-Clause
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $PSScriptRoot "../../artifacts/mewui-source/bootstrap/$([Guid]::NewGuid())"
& dotnet run --project (Join-Path $PSScriptRoot 'MewUiSource.csproj') --no-launch-profile --artifacts-path $artifacts
if ($LASTEXITCODE -ne 0) { throw "MewUI package build failed with exit code $LASTEXITCODE" }
Remove-Item -Recurse -Force $artifacts
