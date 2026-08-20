[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:APPDATA = Join-Path $projectRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $projectRoot '.tools\localappdata'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:ISSUEDROP_DATA_DIR = Join-Path $projectRoot '.tools\verify-data'

& $dotnet restore (Join-Path $projectRoot 'IssueDrop.sln') --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet build (Join-Path $projectRoot 'IssueDrop.sln') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $dotnet run --project (Join-Path $projectRoot 'tests\IssueDrop.Tests\IssueDrop.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

& $dotnet publish (Join-Path $projectRoot 'src\IssueDrop\IssueDrop.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Publish validation failed.' }

Write-Host 'IssueDrop verification passed.' -ForegroundColor Green
