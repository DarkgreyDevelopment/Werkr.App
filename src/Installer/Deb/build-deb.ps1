#Requires -Version 7.2
<#
    .SYNOPSIS
        Build a .deb package from pre-published Werkr binaries and static
        template files.

    .DESCRIPTION
        Standalone .deb build script that reads static DEBIAN/ and systemd/
        template files from src/Installer/Deb/, substitutes build-time values
        (version, architecture), stages published binaries, and produces a
        .deb package via dpkg-deb.

        This script can be invoked directly or called from publish.ps1.

    .EXAMPLE
        ./src/Installer/Deb/build-deb.ps1 -ProductType Agent -BinaryPath ./Publish/Agent -Version 1.0.0 -Architecture amd64

    .EXAMPLE
        ./src/Installer/Deb/build-deb.ps1 -ProductType ServerBundle -BinaryPath ./Publish/ServerBundle -Version 1.0.0 -Architecture arm64 -OutputPath ./Publish
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
    [ValidateSet('amd64', 'arm64')]
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
[string]$ProductLower = $ProductType.ToLower()
[string]$TemplateDir = switch ($ProductType) {
    'ServerBundle' { Join-Path $ScriptRoot 'server' }
    'Agent'        { Join-Path $ScriptRoot 'agent' }
}
[string]$PackageName = switch ($ProductType) {
    'ServerBundle' { 'werkr-server' }
    'Agent'        { 'werkr-agent' }
}

# Default edition name for the .deb filename
if (-not $EditionName) {
    $EditionName = "${PackageName}_${Version}_${Architecture}"
}

Write-Host "Building .deb: $EditionName"
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

# Check for dpkg-deb
$dpkgDeb = Get-Command dpkg-deb -ErrorAction SilentlyContinue
if (-not $dpkgDeb) {
    throw "dpkg-deb is not installed. Install dpkg-dev (apt) or dpkg (brew) first."
}

# Create staging directory
[string]$StagingDir = Join-Path ([System.IO.Path]::GetTempPath()) "werkr-deb-$EditionName"
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }

try {
    # Create directory structure
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir 'DEBIAN')
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "opt/werkr/$ProductLower")
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir 'etc/werkr')
    # Ship a default conffile so the conffiles declaration is satisfied
    Set-Content -Path (Join-Path $StagingDir 'etc/werkr/appsettings.json') -Value '{}' -NoNewline
    if ($IsLinux -or $IsMacOS) { chmod 640 (Join-Path $StagingDir 'etc/werkr/appsettings.json') }
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir 'usr/lib/systemd/system')

    # Agent gets a modules directory
    if ($ProductType -eq 'Agent') {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "opt/werkr/$ProductLower/modules")
    }

    # ---- DEBIAN/control (from template, with substitution) ----
    [string]$ControlTemplate = Get-Content -Path (Join-Path $TemplateDir 'DEBIAN/control.template') -Raw
    [string]$ControlContent = $ControlTemplate `
        -replace '{{VERSION}}', $Version `
        -replace '{{ARCHITECTURE}}', $Architecture
    Set-Content -Path (Join-Path $StagingDir 'DEBIAN/control') -Value $ControlContent -NoNewline

    # ---- Static DEBIAN files ----
    [string[]]$StaticDebianFiles = @('conffiles', 'templates', 'config', 'postinst', 'prerm', 'postrm')
    foreach ($file in $StaticDebianFiles) {
        [string]$src = Join-Path $TemplateDir "DEBIAN/$file"
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination (Join-Path $StagingDir "DEBIAN/$file") -Force
        }
    }

    # ---- Shared rules file ----
    [string]$RulesFile = Join-Path $ScriptRoot 'rules'
    if (Test-Path $RulesFile) {
        Copy-Item -Path $RulesFile -Destination (Join-Path $StagingDir 'DEBIAN/rules') -Force
    }

    # ---- systemd service unit(s) ----
    [string]$SystemdDir = Join-Path $TemplateDir 'systemd'
    if (Test-Path $SystemdDir) {
        Get-ChildItem -Path $SystemdDir -Filter '*.service' | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $StagingDir 'usr/lib/systemd/system/') -Force
        }
    }

    # ---- Set executable permissions on maintainer scripts ----
    if ($IsLinux -or $IsMacOS) {
        [string[]]$ExecutableFiles = @('postinst', 'prerm', 'postrm', 'config', 'rules')
        foreach ($file in $ExecutableFiles) {
            [string]$path = Join-Path $StagingDir "DEBIAN/$file"
            if (Test-Path $path) {
                chmod 755 $path
            }
        }
    }

    # ---- Copy published binaries ----
    Copy-Item -Path (Join-Path $BinaryPath '*') -Destination (Join-Path $StagingDir "opt/werkr/$ProductLower") -Recurse -Force

    # ---- Build the .deb ----
    [string]$DebFile = Join-Path $OutputPath "$EditionName.deb"
    & dpkg-deb --build --root-owner-group $StagingDir $DebFile
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb failed for $EditionName (exit $LASTEXITCODE)"
    }

    Write-Host "Successfully built: $DebFile" -ForegroundColor Green

    # Show package info
    & dpkg-deb --info $DebFile
}
finally {
    # Cleanup staging
    if (Test-Path $StagingDir) {
        Remove-Item -Path $StagingDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
