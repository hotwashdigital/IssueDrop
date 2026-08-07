[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$output = Join-Path $projectRoot "artifacts\IssueDrop-$Runtime"
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:APPDATA = Join-Path $projectRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $projectRoot '.tools\localappdata'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }

& $dotnet restore (Join-Path $projectRoot 'src\IssueDrop\IssueDrop.csproj') -r $Runtime -p:SelfContained=true --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet publish (Join-Path $projectRoot 'src\IssueDrop\IssueDrop.csproj') -c Release -r $Runtime --self-contained true -o $output --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $output 'README.md') -Force
$zipPath = Join-Path $projectRoot "artifacts\IssueDrop-$Runtime.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "IssueDrop published to $output" -ForegroundColor Green
Write-Host "Portable ZIP: $zipPath" -ForegroundColor Green
