[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [switch]$SkipInstaller,
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
if ($SkipInstaller -and $RequireInstaller) {
    throw '-SkipInstaller and -RequireInstaller cannot be used together.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildPropsPath = Join-Path $projectRoot 'Directory.Build.props'
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content -LiteralPath $buildPropsPath
    $versionProperty = $buildProps.Project.PropertyGroup |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Version) } |
        Select-Object -First 1
    $Version = ([string]$versionProperty.Version).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid release version."
}

$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$artifacts = Join-Path $projectRoot 'artifacts'
$output = Join-Path $artifacts "IssueDrop-$Runtime"
$zipPath = Join-Path $artifacts "IssueDrop-$Runtime.zip"
$setupPath = Join-Path $artifacts "IssueDrop-Setup-$Version.exe"
$checksumPath = Join-Path $artifacts 'SHA256SUMS.txt'
$nugetConfig = Join-Path $projectRoot 'NuGet.Config'
$project = Join-Path $projectRoot 'src\IssueDrop\IssueDrop.csproj'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:APPDATA = Join-Path $projectRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $projectRoot '.tools\localappdata'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'

if (-not (Test-Path -LiteralPath $artifacts)) {
    New-Item -ItemType Directory -Path $artifacts | Out-Null
}
if (Test-Path -LiteralPath $output) {
    $resolvedOutput = (Resolve-Path -LiteralPath $output).Path
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifacts).Path
    if (-not $resolvedOutput.StartsWith($resolvedArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove output outside the artifacts directory: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

& $dotnet restore $project -r $Runtime -p:SelfContained=true --configfile $nugetConfig -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $dotnet publish $project -c Release -r $Runtime --self-contained true -o $output --no-restore -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

foreach ($file in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $output $file) -Force
}
$dotnetExecutable = (Get-Command $dotnet -ErrorAction Stop).Source
$dotnetRoot = Split-Path -Parent $dotnetExecutable
$dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
if (Test-Path -LiteralPath $dotnetLicense) {
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $output 'DOTNET-LICENSE.txt') -Force
}
if (Test-Path -LiteralPath $dotnetNotices) {
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $output 'THIRD-PARTY-NOTICES.txt') -Force
}

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zipPath -CompressionLevel Optimal

$installerBuilt = $false
if (-not $SkipInstaller) {
    $innoCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $iscc = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($iscc) {
        if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
        $installerScript = Join-Path $projectRoot 'installer\IssueDrop.iss'
        & $iscc "/DMyAppVersion=$Version" "/DSourceDir=$output" "/DOutputDir=$artifacts" $installerScript
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setupPath)) { throw 'Installer build failed.' }
        $installerBuilt = $true
    }
    elseif ($RequireInstaller) {
        throw 'Inno Setup 6 was not found. Install it or omit -RequireInstaller.'
    }
    else {
        Write-Warning 'Inno Setup 6 was not found; the portable ZIP was built without a Setup executable.'
    }
}

$releaseFiles = @($zipPath)
if ($installerBuilt) { $releaseFiles += $setupPath }
$checksumLines = foreach ($file in $releaseFiles) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($file))"
}
$checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "IssueDrop $Version published to $output" -ForegroundColor Green
Write-Host "Portable ZIP: $zipPath" -ForegroundColor Green
if ($installerBuilt) { Write-Host "Windows installer: $setupPath" -ForegroundColor Green }
Write-Host "Checksums: $checksumPath" -ForegroundColor Green
