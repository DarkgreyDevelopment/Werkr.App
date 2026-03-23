#Requires -Version 7.2
<#
    .SYNOPSIS
        Build a macOS .pkg installer from pre-published Werkr binaries and
        static template files.

    .DESCRIPTION
        Standalone .pkg build script that reads static launchd plists, installer
        scripts, and distribution.xml from src/Installer/Pkg/, stages published
        binaries into the /Library/Werkr/ layout, and produces a product .pkg
        via pkgbuild + productbuild.

        This script can be invoked directly or called from publish.ps1.

    .EXAMPLE
        ./src/Installer/Pkg/build-pkg.ps1 -ProductType Agent -BinaryPath ./Publish/Agent -Version 1.0.0 -Architecture arm64

    .EXAMPLE
        ./src/Installer/Pkg/build-pkg.ps1 -ProductType ServerBundle -BinaryPath ./Publish/ServerBundle -Version 1.0.0 -Architecture x64 -OutputPath ./Publish
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [ValidateSet('Agent', 'ServerBundle')]
    [string]$ProductType,

    [Parameter(Mandatory)]
    [string]$BinaryPath,

    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = '.',

    [Parameter(Mandatory = $false)]
    [string]$EditionName
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Resolve paths
[string]$ScriptRoot = $PSScriptRoot
[string]$BinaryPath = (Resolve-Path $BinaryPath).Path
[string]$OutputPath = (Resolve-Path $OutputPath).Path

# Product-specific configuration
[string]$TemplateDir = switch ($ProductType) {
    'ServerBundle' { Join-Path $ScriptRoot 'server' }
    'Agent'        { Join-Path $ScriptRoot 'agent' }
}
[string]$PackageIdentifier = switch ($ProductType) {
    'ServerBundle' { 'app.werkr.server' }
    'Agent'        { 'app.werkr.agent' }
}
[string]$InstallSubDir = switch ($ProductType) {
    'ServerBundle' { 'Server' }
    'Agent'        { 'Agent' }
}

# Default edition name for the .pkg filename
if (-not $EditionName) {
    [string]$ArchLabel = switch ($Architecture) {
        'arm64' { 'osx-arm64' }
        'x64'   { 'osx-x64' }
    }
    $EditionName = "werkr-$($InstallSubDir.ToLower())-${Version}-${ArchLabel}"
}

Write-Host "Building .pkg: $EditionName"
Write-Host "  Product:      $ProductType"
Write-Host "  Version:      $Version"
Write-Host "  Architecture: $Architecture"
Write-Host "  Binaries:     $BinaryPath"
Write-Host "  Templates:    $TemplateDir"
Write-Host "  Output:       $OutputPath"

# Validate inputs
if (-not (Test-Path $BinaryPath)) {
    throw "Binary path does not exist: $BinaryPath"
}
if (-not (Test-Path $TemplateDir)) {
    throw "Template directory does not exist: $TemplateDir"
}

# Check for pkgbuild and productbuild (require Xcode Command Line Tools)
foreach ($tool in @('pkgbuild', 'productbuild')) {
    $cmd = Get-Command $tool -ErrorAction SilentlyContinue
    if (-not $cmd) {
        throw "$tool is not installed. Install Xcode Command Line Tools: xcode-select --install"
    }
}

# Create staging directory
[string]$StagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "werkr-pkg-$EditionName"
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }

# Temp directory for intermediate component .pkg
[string]$ComponentDir = Join-Path ([System.IO.Path]::GetTempPath()) "werkr-pkg-component-$EditionName"
if (Test-Path $ComponentDir) { Remove-Item $ComponentDir -Recurse -Force }
$null = New-Item -ItemType Directory -Force -Path $ComponentDir

try {
    # ---- Stage payload into install layout ----
    # Payload root mirrors the macOS filesystem — pkgbuild --install-location /
    # places these files at their absolute paths on the target system.
    [string]$InstallDir = Join-Path $StagingDir 'Library' 'Werkr' $InstallSubDir
    $null = New-Item -ItemType Directory -Force -Path $InstallDir

    # Copy published binaries
    Copy-Item -Path (Join-Path $BinaryPath '*') -Destination $InstallDir -Recurse -Force

    # Copy launchd plists into payload (postinstall copies them to /Library/LaunchDaemons/)
    [string]$LaunchdSrc = Join-Path $TemplateDir 'launchd'
    [string]$LaunchdDst = Join-Path $InstallDir 'launchd'
    $null = New-Item -ItemType Directory -Force -Path $LaunchdDst
    Copy-Item -Path (Join-Path $LaunchdSrc '*.plist') -Destination $LaunchdDst -Force

    # Copy uninstall script into payload
    [string]$UninstallScript = switch ($ProductType) {
        'ServerBundle' { Join-Path $ScriptRoot 'uninstall-werkr-server.sh' }
        'Agent'        { Join-Path $ScriptRoot 'uninstall-werkr-agent.sh' }
    }
    if (Test-Path $UninstallScript) {
        Copy-Item -Path $UninstallScript -Destination $InstallDir -Force
    }

    # Agent gets a modules directory
    if ($ProductType -eq 'Agent') {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $InstallDir 'modules')
    }

    # ---- Prepare scripts directory ----
    [string]$ScriptsDir = Join-Path $TemplateDir 'scripts'
    if (-not (Test-Path $ScriptsDir)) {
        throw "Scripts directory does not exist: $ScriptsDir"
    }

    # ---- Prepare distribution.xml with version substitution ----
    [string]$DistSrc = Join-Path $TemplateDir 'distribution.xml'
    if (-not (Test-Path $DistSrc)) {
        throw "distribution.xml does not exist: $DistSrc"
    }
    [string]$DistContent = (Get-Content -Path $DistSrc -Raw) -replace '{{VERSION}}', $Version
    [string]$DistDst = Join-Path $ComponentDir 'distribution.xml'
    Set-Content -Path $DistDst -Value $DistContent -NoNewline

    # ---- Prepare resources directory ----
    [string]$ResourcesDir = Join-Path $ScriptRoot 'resources'

    # ---- Build component package with pkgbuild ----
    [string]$ComponentPkg = Join-Path $ComponentDir "$PackageIdentifier.pkg"

    [string[]]$PkgBuildArgs = @(
        '--root', $StagingDir,
        '--identifier', $PackageIdentifier,
        '--version', $Version,
        '--install-location', '/',
        '--scripts', $ScriptsDir,
        $ComponentPkg
    )

    Write-Host "Running: pkgbuild $($PkgBuildArgs -join ' ')"
    & pkgbuild @PkgBuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "pkgbuild failed (exit $LASTEXITCODE)"
    }

    # ---- Build product package with productbuild ----
    [string]$ProductPkg = Join-Path $OutputPath "$EditionName.pkg"

    [string[]]$ProductBuildArgs = @(
        '--distribution', $DistDst,
        '--package-path', $ComponentDir
    )

    # Add resources if directory exists and has content
    if ((Test-Path $ResourcesDir) -and (Get-ChildItem -Path $ResourcesDir | Measure-Object).Count -gt 0) {
        $ProductBuildArgs += @('--resources', $ResourcesDir)
    }

    $ProductBuildArgs += $ProductPkg

    Write-Host "Running: productbuild $($ProductBuildArgs -join ' ')"
    & productbuild @ProductBuildArgs
    if ($LASTEXITCODE -ne 0) {
        throw "productbuild failed (exit $LASTEXITCODE)"
    }

    Write-Host "Successfully built: $ProductPkg" -ForegroundColor Green

    # Show package info
    & pkgutil --payload-files $ComponentPkg | Select-Object -First 20
    Write-Host '  ...' -ForegroundColor DarkGray
}
finally {
    # Cleanup staging and component directories
    foreach ($dir in @($StagingDir, $ComponentDir)) {
        if (Test-Path $dir) {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
