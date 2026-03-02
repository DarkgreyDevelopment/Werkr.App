#Requires -Version 7.2
using namespace System.IO
<#
    .SYNOPSIS
        Build, publish, and package Werkr products for all supported platforms.

    .DESCRIPTION
        This script publishes Werkr applications as self-contained, single-file
        executables and optionally creates platform-specific installers:
          - Windows  : MSI (WiX 6 SDK-style)
          - Linux    : .deb package with debconf, systemd service, non-root user
          - macOS    : .app bundle with launcher script

        GitVersion is used for semantic versioning.  If dotnet-gitversion is not
        available or the workspace is not a git repo, the script falls back to
        version 0.0.1-local.

    .EXAMPLE
        ./scripts/publish.ps1 -Application Agent -Platform linux -Architecture arm64 -BuildDebInstallers
    .EXAMPLE
        ./scripts/publish.ps1 -Verbose
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [ValidateSet('All', 'ServerBundle', 'Agent')]
    [string]$Application = 'All',

    [Parameter(Mandatory = $false)]
    [ValidateSet('All', 'x64', 'arm64')]
    [string]$Architecture = 'All',

    [Parameter(Mandatory = $false)]
    [ValidateSet('All', 'windows', 'linux', 'macos')]
    [string]$Platform = 'All',

    [Parameter(Mandatory = $false)]
    [switch]$BuildMsiInstallers,

    [Parameter(Mandatory = $false)]
    [switch]$BuildDebInstallers,

    [Parameter(Mandatory = $false)]
    [switch]$BuildMacOSPackage,

    [Parameter(Mandatory = $false)]
    [switch]$SkipCompression,

    [Parameter(Mandatory = $false)]
    [switch]$SkipTar
)
$ErrorActionPreference = 'Stop'
[bool]$Verbose = ($PSBoundParameters.ContainsKey('Verbose')) ? $PSBoundParameters['Verbose'] : $false
Set-StrictMode -Version Latest

#region functions

function Assert-DotnetInstalled {
<#
    .SYNOPSIS
        Assert that dotnet is installed and meets the minimum version requirement.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [int]$DotNetVersion
    )

    [string]$DotNetCommand = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ([string]::IsNullOrWhiteSpace($DotNetCommand)) {
        throw "dotnet SDK is not installed. Install .NET $DotNetVersion SDK from https://dotnet.microsoft.com/download"
    }

    [version]$InstalledVersion = & dotnet --version | ForEach-Object { [version]::new($_) }
    if ($InstalledVersion.Major -lt $DotNetVersion) {
        throw "dotnet $($InstalledVersion) does not meet the minimum version ($DotNetVersion). Update from https://dotnet.microsoft.com/download"
    }
    Write-Verbose "dotnet $InstalledVersion OK (minimum $DotNetVersion)"
}

function Assert-DpkgDebInstalled {
<#
    .SYNOPSIS
        Assert that dpkg-deb is available when .deb installers are requested.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [bool]$BuildDebInstallers
    )

    if (-not $BuildDebInstallers) { return }

    [string]$DpkgDeb = Get-Command dpkg-deb -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ([string]::IsNullOrWhiteSpace($DpkgDeb)) {
        throw 'dpkg-deb is not installed. On Ubuntu/Debian: sudo apt-get install dpkg'
    }
    Write-Verbose "dpkg-deb found at $DpkgDeb"
}

function Assert-TarInstalled {
<#
    .SYNOPSIS
        Assert that tar is available when .tar.gz output is requested.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [bool]$SkipTar
    )

    if ($SkipTar) { return }
    if (-not ($IsLinux -or $IsMacOS)) { return }

    [string]$Tar = Get-Command tar -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ([string]::IsNullOrWhiteSpace($Tar)) {
        throw 'tar is not installed. Install tar or use -SkipTar.'
    }
    Write-Verbose "tar found at $Tar"
}

function Get-GitVersion {
<#
    .SYNOPSIS
        Obtain semantic version from GitVersion.  Falls back to 0.0.1-local.
    .OUTPUTS
        [hashtable] with keys: SemVer, MajorMinorPatch, Major, Minor, Patch, PreReleaseTag, InformationalVersion
#>
    [CmdletBinding()]
    [OutputType([hashtable])]
    param ()

    try {
        [string]$RawJson = & dotnet gitversion /output json 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'gitversion exited with non-zero' }
        $GV = $RawJson | ConvertFrom-Json
        return @{
            SemVer                 = $GV.SemVer
            MajorMinorPatch        = $GV.MajorMinorPatch
            Major                  = $GV.Major
            Minor                  = $GV.Minor
            Patch                  = $GV.Patch
            PreReleaseTag          = $GV.PreReleaseTag
            InformationalVersion   = $GV.InformationalVersion
        }
    }
    catch {
        Write-Warning "GitVersion unavailable — using fallback 0.0.1-local. ($_)"
        return @{
            SemVer                 = '0.0.1-local'
            MajorMinorPatch        = '0.0.1'
            Major                  = 0
            Minor                  = 0
            Patch                  = 1
            PreReleaseTag          = 'local'
            InformationalVersion   = '0.0.1-local'
        }
    }
}

function Set-GrpcToolsArm64Directory {
<#
    .SYNOPSIS
        When cross-compiling for Windows ARM64, point the Grpc.Tools package at
        the x64 native tools (Grpc.Tools ships no ARM64 protoc for Windows).
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [string]$ProjectPath,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [string]$Arch
    )

    if ($Arch -ine 'arm64' -or $RuntimeIdentifier -inotlike 'win-*') { return }

    # NuGet global-packages cache
    [string]$NuGetGlobalPackages = & dotnet nuget locals global-packages --list |
        ForEach-Object { ($_ -split ':\s*', 2)[1] } |
        Select-Object -First 1

    [string]$GrpcToolsDir = Get-ChildItem -Path (Join-Path $NuGetGlobalPackages 'grpc.tools') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1 -ExpandProperty FullName

    if ([string]::IsNullOrWhiteSpace($GrpcToolsDir)) {
        Write-Warning 'Could not locate Grpc.Tools package — skipping ARM64 override.'
        return
    }

    [Environment]::SetEnvironmentVariable('GRPC_PROTOC_PLUGIN_DIR',
        (Join-Path $GrpcToolsDir 'tools' 'windows_x64'))
    Write-Verbose "GRPC_PROTOC_PLUGIN_DIR → $(Join-Path $GrpcToolsDir 'tools' 'windows_x64')"
}

function New-Executable {
<#
    .SYNOPSIS
        Publish a self-contained, single-file executable using dotnet publish.
#>
    [CmdletBinding()]
    [OutputType([int])]
    param (
        [Parameter(Mandatory)]
        [string]$OutputPath,

        [Parameter(Mandatory)]
        [string]$ProjectPath,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [hashtable]$VersionInfo,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    Write-Host "[$Counter] Publishing $RuntimeIdentifier → $OutputPath"
    [string[]]$PublishArgs = @(
        'publish'
        $ProjectPath
        '-c', 'Release'
        '-r', $RuntimeIdentifier
        '-o', $OutputPath
        '--sc', 'true'
        '-p:PublishSingleFile=true'
        "-p:Version=$($VersionInfo.MajorMinorPatch)"
        "-p:AssemblyVersion=$($VersionInfo.Major).$($VersionInfo.Minor).$($VersionInfo.Patch).0"
        "-p:FileVersion=$($VersionInfo.Major).$($VersionInfo.Minor).$($VersionInfo.Patch).0"
        "-p:InformationalVersion=$($VersionInfo.InformationalVersion)"
    )
    & dotnet @PublishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $RuntimeIdentifier (exit $LASTEXITCODE)" }

    return $Counter + 1
}

function Build-Installer {
<#
    .SYNOPSIS
        Dispatch to the appropriate installer builder based on OS.
#>
    [CmdletBinding()]
    [OutputType([int])]
    param (
        [Parameter(Mandatory)]
        [string]$OS,

        [Parameter(Mandatory)]
        [bool]$BuildMsiInstallers,

        [Parameter(Mandatory)]
        [bool]$BuildDebInstallers,

        [Parameter(Mandatory)]
        [bool]$BuildMacOSPackage,

        [Parameter(Mandatory)]
        [string]$ProductType,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [string]$Arch,

        [Parameter(Mandatory)]
        [hashtable]$VersionInfo,

        [Parameter(Mandatory)]
        [string]$EditionName,

        [Parameter(Mandatory)]
        [string]$OutputPath,

        [Parameter(Mandatory)]
        [string]$PublishPath,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    switch ($OS) {
        'windows' {
            if ($BuildMsiInstallers) {
                $MsiPackageParams = @{
                    ProductType       = $ProductType
                    RuntimeIdentifier = $RuntimeIdentifier
                    Arch              = $Arch
                    VersionInfo       = $VersionInfo
                    EditionName       = $EditionName
                    PublishPath       = $PublishPath
                    Counter           = $Counter
                    Verbose           = $Verbose
                }
                $Counter = New-MsiInstaller @MsiPackageParams
            }
        }
        'linux' {
            if ($BuildDebInstallers) {
                $DebPackageParams = @{
                    ProductType       = $ProductType
                    RuntimeIdentifier = $RuntimeIdentifier
                    VersionInfo       = $VersionInfo
                    EditionName       = $EditionName
                    OutputPath        = $OutputPath
                    PublishPath       = $PublishPath
                    Counter           = $Counter
                    Verbose           = $Verbose
                }
                $Counter = New-DebPackage @DebPackageParams
            }
        }
        'macos' {
            if ($BuildMacOSPackage) {
                $MacOSPackageParams = @{
                    ProductType = $ProductType
                    VersionInfo = $VersionInfo
                    EditionName = $EditionName
                    OutputPath  = $OutputPath
                    PublishPath = $PublishPath
                    Counter     = $Counter
                    Verbose     = $Verbose
                }
                $Counter = New-MacPackage @MacOSPackageParams
            }
        }
    }
    return $Counter
}

function New-MsiInstaller {
<#
    .SYNOPSIS
        Build a WiX 6 SDK-style MSI installer.
        WiX 6 is auto-resolved via the SDK-style .wixproj — no separate
        wix.exe install is required.
#>
    [CmdletBinding()]
    [OutputType([int])]
    param (
        [Parameter(Mandatory)]
        [string]$ProductType,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [string]$Arch,

        [Parameter(Mandatory)]
        [hashtable]$VersionInfo,

        [Parameter(Mandatory)]
        [string]$EditionName,

        [Parameter(Mandatory)]
        [string]$PublishPath,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    [string]$WixArch = $Arch -ieq 'x64' ? 'x64' : 'arm64'
    [string]$InstallerDir = Join-Path -Path $RepoRoot -ChildPath 'src' -AdditionalChildPath 'Installer', 'Msi', $ProductType
    [string]$WixProj = Join-Path -Path $InstallerDir -ChildPath "$ProductType.wixproj"

    if (-not (Test-Path $WixProj)) {
        Write-Warning "WiX project not found: $WixProj — skipping MSI for $ProductType"
        return $Counter
    }

    Write-Host "[$Counter] Building MSI: $EditionName ($WixArch)"
    [string[]]$BuildArgs = @(
        'build'
        $WixProj
        '-c', 'Release'
        "-p:Platform=$WixArch"
        "-p:ProductVersion=$($VersionInfo.MajorMinorPatch)"
        "-p:RuntimeIdentifier=$RuntimeIdentifier"
        '-nologo'
    )
    & dotnet @BuildArgs
    if ($LASTEXITCODE -ne 0) { throw "WiX build failed for $EditionName (exit $LASTEXITCODE)" }

    # Move the MSI to the Publish folder
    [string]$MsiOutputDir = Join-Path -Path $InstallerDir -ChildPath 'bin' -AdditionalChildPath 'Release'
    [string]$MsiFile = Get-ChildItem -Path $MsiOutputDir -Filter '*.msi' -Recurse |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName

    if ([string]::IsNullOrWhiteSpace($MsiFile)) {
        Write-Warning "MSI file not found in $MsiOutputDir"
        return $Counter + 1
    }

    [string]$MsiDest = Join-Path -Path $PublishPath -ChildPath "$EditionName.msi"
    # If this is a pre-release build, append the tag
    if (-not [string]::IsNullOrWhiteSpace($VersionInfo.PreReleaseTag)) {
        $MsiDest = Join-Path -Path $PublishPath -ChildPath "$EditionName.msi"
    }
    Move-Item -Path $MsiFile -Destination $MsiDest -Force -Verbose:$Verbose

    return $Counter + 1
}

function New-DebPackage {
<#
    .SYNOPSIS
        Create a .deb package with debconf, systemd service unit, and non-root
        service user.  The package installs to /opt/werkr/<product>/ and creates
        a werkr system user/group.
#>
    [CmdletBinding()]
    [OutputType([int])]
    param (
        [Parameter(Mandatory)]
        [string]$ProductType,

        [Parameter(Mandatory)]
        [string]$RuntimeIdentifier,

        [Parameter(Mandatory)]
        [hashtable]$VersionInfo,

        [Parameter(Mandatory)]
        [string]$EditionName,

        [Parameter(Mandatory)]
        [string]$OutputPath,

        [Parameter(Mandatory)]
        [string]$PublishPath,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    Write-Host "[$Counter] Building deb: $EditionName"

    [string]$ProductLower = $ProductType.ToLower()
    # For ServerBundle the package name is werkr-server
    [string]$PackageName = switch ($ProductType) {
        'ServerBundle' { 'werkr-server' }
        'Agent'        { 'werkr-agent' }
        default        { "werkr-$ProductLower" }
    }
    [string]$ServiceName = $PackageName
    [string]$InstallDir = "/opt/werkr/$ProductLower"
    [string]$ConfigDir = "/etc/werkr"

    # Determine the main binary name
    [string]$BinaryName = switch ($ProductType) {
        'ServerBundle' { 'Werkr.Server' }
        'Agent'        { 'Werkr.Agent' }
        default        { "Werkr.$ProductType" }
    }

    # Create staging structure
    [string]$StagingDir = Join-Path -Path ([Path]::GetTempPath()) -ChildPath "werkr-deb-$EditionName"
    if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }

    # Directories
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir 'DEBIAN')
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "opt/werkr/$ProductLower")
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir 'etc/werkr')
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "lib/systemd/system")

    # ---- DEBIAN/control ----
    [string]$DebArch = $RuntimeIdentifier -match 'arm64' ? 'arm64' : 'amd64'
    [string]$Description = switch ($ProductType) {
        'ServerBundle' { 'Werkr Server — Blazor UI + REST/gRPC API' }
        'Agent'        { 'Werkr Agent — background agent with gRPC and PowerShell' }
        default        { "Werkr $ProductType" }
    }
    @"
Package: $PackageName
Version: $($VersionInfo.MajorMinorPatch)
Section: admin
Priority: optional
Architecture: $DebArch
Depends: libicu74 | libicu72 | libicu70, libssl3 | libssl3t64
Maintainer: Werkr <support@werkr.app>
Description: $Description
Homepage: https://werkr.app
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/control') -NoNewline

    # ---- DEBIAN/conffiles ----
    @"
/etc/werkr/appsettings.json
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/conffiles') -NoNewline

    # ---- DEBIAN/templates (debconf) ----
    if ($ProductType -ieq 'ServerBundle') {
        @"
Template: $PackageName/config-path
Type: string
Default: $ConfigDir
Description: Configuration directory for $PackageName
 The directory where $PackageName stores its configuration files.
 The default is $ConfigDir.

Template: $PackageName/install-components
Type: select
Choices: all, server-only, api-only
Default: all
Description: Which components to enable
 Select which Werkr components to enable via systemd services.
 Both Server and Api binaries are always installed. This controls
 which systemd services are enabled on install.
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/templates') -NoNewline
    }
    else {
        @"
Template: $PackageName/config-path
Type: string
Default: $ConfigDir
Description: Configuration directory for $PackageName
 The directory where $PackageName stores its configuration files.
 The default is $ConfigDir.
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/templates') -NoNewline
    }

    # ---- DEBIAN/config (debconf) ----
    if ($ProductType -ieq 'ServerBundle') {
        @"
#!/bin/sh
set -e
. /usr/share/debconf/confmodule
db_input medium $PackageName/config-path || true
db_input medium $PackageName/install-components || true
db_go || true
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/config') -NoNewline
    }
    else {
        @"
#!/bin/sh
set -e
. /usr/share/debconf/confmodule
db_input medium $PackageName/config-path || true
db_go || true
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/config') -NoNewline
    }

    # ---- DEBIAN/postinst ----
    if ($ProductType -ieq 'ServerBundle') {
        @"
#!/bin/sh
set -e

# Create werkr system user and group
if ! getent group werkr >/dev/null 2>&1; then
    groupadd --system werkr
fi
if ! getent passwd werkr >/dev/null 2>&1; then
    useradd --system --gid werkr --no-create-home --shell /usr/sbin/nologin werkr
fi

# Ensure directories exist with correct ownership
mkdir -p $ConfigDir
mkdir -p /var/lib/werkr
mkdir -p /var/log/werkr
chown -R werkr:werkr $InstallDir
chown -R werkr:werkr $ConfigDir
chown -R werkr:werkr /var/lib/werkr
chown -R werkr:werkr /var/log/werkr

# Create default config if it doesn't exist
if [ ! -f "$ConfigDir/appsettings.json" ]; then
    echo '{}' > "$ConfigDir/appsettings.json"
    chown werkr:werkr "$ConfigDir/appsettings.json"
    chmod 640 "$ConfigDir/appsettings.json"
fi

# debconf: read config path and install-components
. /usr/share/debconf/confmodule
db_get $PackageName/config-path || true
WERKR_CONFIG_PATH="\$RET"
db_get $PackageName/install-components || true
INSTALL_COMPONENTS="\$RET"

# Enable services based on install-components selection
systemctl daemon-reload

case "\$INSTALL_COMPONENTS" in
    server-only)
        mkdir -p /etc/systemd/system/werkr-server.service.d
        cat > /etc/systemd/system/werkr-server.service.d/override.conf << EOF
[Service]
Environment=WERKR_CONFIG_PATH=\$WERKR_CONFIG_PATH
EOF
        systemctl enable werkr-server.service || true
        systemctl restart werkr-server.service || true
        ;;
    api-only)
        mkdir -p /etc/systemd/system/werkr-api.service.d
        cat > /etc/systemd/system/werkr-api.service.d/override.conf << EOF
[Service]
Environment=WERKR_CONFIG_PATH=\$WERKR_CONFIG_PATH
EOF
        systemctl enable werkr-api.service || true
        systemctl restart werkr-api.service || true
        ;;
    *)
        # all — enable both
        mkdir -p /etc/systemd/system/werkr-server.service.d
        cat > /etc/systemd/system/werkr-server.service.d/override.conf << EOF
[Service]
Environment=WERKR_CONFIG_PATH=\$WERKR_CONFIG_PATH
EOF
        mkdir -p /etc/systemd/system/werkr-api.service.d
        cat > /etc/systemd/system/werkr-api.service.d/override.conf << EOF
[Service]
Environment=WERKR_CONFIG_PATH=\$WERKR_CONFIG_PATH
EOF
        systemctl enable werkr-server.service || true
        systemctl restart werkr-server.service || true
        systemctl enable werkr-api.service || true
        systemctl restart werkr-api.service || true
        ;;
esac

#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/postinst') -NoNewline
    }
    else {
        @"
#!/bin/sh
set -e

# Create werkr system user and group
if ! getent group werkr >/dev/null 2>&1; then
    groupadd --system werkr
fi
if ! getent passwd werkr >/dev/null 2>&1; then
    useradd --system --gid werkr --no-create-home --shell /usr/sbin/nologin werkr
fi

# Ensure directories exist with correct ownership
mkdir -p $ConfigDir
mkdir -p /var/lib/werkr
mkdir -p /var/log/werkr
chown -R werkr:werkr $InstallDir
chown -R werkr:werkr $ConfigDir
chown -R werkr:werkr /var/lib/werkr
chown -R werkr:werkr /var/log/werkr

# Create default config if it doesn't exist
if [ ! -f "$ConfigDir/appsettings.json" ]; then
    echo '{}' > "$ConfigDir/appsettings.json"
    chown werkr:werkr "$ConfigDir/appsettings.json"
    chmod 640 "$ConfigDir/appsettings.json"
fi

# debconf: read config path
. /usr/share/debconf/confmodule
db_get $PackageName/config-path || true
WERKR_CONFIG_PATH="\$RET"

# Update systemd environment override
mkdir -p /etc/systemd/system/$ServiceName.service.d
cat > /etc/systemd/system/$ServiceName.service.d/override.conf << EOF
[Service]
Environment=WERKR_CONFIG_PATH=\$WERKR_CONFIG_PATH
EOF

# Enable and restart service
systemctl daemon-reload
systemctl enable $ServiceName.service || true
systemctl restart $ServiceName.service || true

#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/postinst') -NoNewline
    }

    # ---- DEBIAN/prerm ----
    if ($ProductType -ieq 'ServerBundle') {
        @"
#!/bin/sh
set -e
systemctl stop werkr-server.service || true
systemctl stop werkr-api.service || true
#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/prerm') -NoNewline
    }
    else {
        @"
#!/bin/sh
set -e
systemctl stop $ServiceName.service || true
#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/prerm') -NoNewline
    }

    # ---- DEBIAN/postrm ----
    if ($ProductType -ieq 'ServerBundle') {
        @"
#!/bin/sh
set -e

case "`$1" in
    purge)
        # Remove config, data, logs, and system user
        rm -rf $ConfigDir
        rm -rf /var/lib/werkr
        rm -rf /var/log/werkr
        rm -rf $InstallDir
        rm -rf /etc/systemd/system/werkr-server.service.d
        rm -rf /etc/systemd/system/werkr-api.service.d
        userdel werkr 2>/dev/null || true
        groupdel werkr 2>/dev/null || true
        systemctl daemon-reload
        ;;
    remove)
        systemctl daemon-reload
        ;;
esac

#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/postrm') -NoNewline
    }
    else {
        @"
#!/bin/sh
set -e

case "`$1" in
    purge)
        # Remove config, data, logs, and system user
        rm -rf $ConfigDir
        rm -rf /var/lib/werkr
        rm -rf /var/log/werkr
        rm -rf $InstallDir
        rm -rf /etc/systemd/system/$ServiceName.service.d
        userdel werkr 2>/dev/null || true
        groupdel werkr 2>/dev/null || true
        systemctl daemon-reload
        ;;
    remove)
        systemctl daemon-reload
        ;;
esac

#DEBHELPER#
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/postrm') -NoNewline
    }

    # ---- systemd service unit(s) ----
    if ($ProductType -ieq 'ServerBundle') {
        # Server service
        @"
[Unit]
Description=Werkr Server — Blazor UI
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=$InstallDir/Werkr.Server
WorkingDirectory=$InstallDir
Restart=on-failure
RestartSec=10
User=werkr
Group=werkr
Environment=DOTNET_ENVIRONMENT=Production
Environment=WERKR_CONFIG_PATH=$ConfigDir
Environment=WERKR_DATA_DIR=/var/lib/werkr
KillSignal=SIGTERM
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
"@ | Set-Content -Path (Join-Path $StagingDir 'lib/systemd/system/werkr-server.service') -NoNewline

        # Api service
        @"
[Unit]
Description=Werkr API — REST/gRPC API
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=$InstallDir/Werkr.Api
WorkingDirectory=$InstallDir
Restart=on-failure
RestartSec=10
User=werkr
Group=werkr
Environment=DOTNET_ENVIRONMENT=Production
Environment=WERKR_CONFIG_PATH=$ConfigDir
Environment=WERKR_DATA_DIR=/var/lib/werkr
KillSignal=SIGTERM
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
"@ | Set-Content -Path (Join-Path $StagingDir 'lib/systemd/system/werkr-api.service') -NoNewline
    }
    else {
        @"
[Unit]
Description=$Description
After=network-online.target
Wants=network-online.target

[Service]
Type=notify
ExecStart=$InstallDir/$BinaryName
WorkingDirectory=$InstallDir
Restart=on-failure
RestartSec=10
User=werkr
Group=werkr
Environment=DOTNET_ENVIRONMENT=Production
Environment=WERKR_CONFIG_PATH=$ConfigDir
Environment=WERKR_DATA_DIR=/var/lib/werkr
KillSignal=SIGTERM
TimeoutStopSec=30

[Install]
WantedBy=multi-user.target
"@ | Set-Content -Path (Join-Path $StagingDir "lib/systemd/system/$ServiceName.service") -NoNewline
    }

    # ---- DEBIAN/rules ----
    @"
#!/usr/bin/make -f
%:
`tdh `$@ --with systemd
override_dh_shlibdeps:
override_dh_strip:
"@ | Set-Content -Path (Join-Path $StagingDir 'DEBIAN/rules') -NoNewline

    # Set executable permissions on maintainer scripts
    if ($IsLinux -or $IsMacOS) {
        chmod 755 (Join-Path $StagingDir 'DEBIAN/postinst')
        chmod 755 (Join-Path $StagingDir 'DEBIAN/prerm')
        chmod 755 (Join-Path $StagingDir 'DEBIAN/postrm')
        chmod 755 (Join-Path $StagingDir 'DEBIAN/config')
        chmod 755 (Join-Path $StagingDir 'DEBIAN/rules')
    }

    # Copy published binaries
    Copy-Item -Path (Join-Path $OutputPath '*') -Destination (Join-Path $StagingDir "opt/werkr/$ProductLower") -Recurse -Force

    # Build the .deb
    [string]$DebFile = Join-Path -Path $PublishPath -ChildPath "$EditionName.deb"
    & dpkg-deb --build --root-owner-group $StagingDir $DebFile
    if ($LASTEXITCODE -ne 0) { throw "dpkg-deb failed for $EditionName (exit $LASTEXITCODE)" }

    # Cleanup staging
    Remove-Item -Path $StagingDir -Recurse -Force -ErrorAction SilentlyContinue

    return $Counter + 1
}

function New-MacPackage {
<#
    .SYNOPSIS
        Create a macOS .app bundle.  For ServerBundle, a launcher shell script
        starts both Server and Api processes.
#>
    [CmdletBinding()]
    [OutputType([int])]
    param (
        [Parameter(Mandatory)]
        [string]$ProductType,

        [Parameter(Mandatory)]
        [hashtable]$VersionInfo,

        [Parameter(Mandatory)]
        [string]$EditionName,

        [Parameter(Mandatory)]
        [string]$OutputPath,

        [Parameter(Mandatory)]
        [string]$PublishPath,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    Write-Host "[$Counter] Building macOS package: $EditionName"

    [string]$DisplayName = switch ($ProductType) {
        'ServerBundle' { 'Werkr Server' }
        'Agent'        { 'Werkr Agent' }
        default        { "Werkr $ProductType" }
    }

    [string]$BundleIdentifier = switch ($ProductType) {
        'ServerBundle' { 'app.werkr.server' }
        'Agent'        { 'app.werkr.agent' }
        default        { "app.werkr.$($ProductType.ToLower())" }
    }

    [string]$AppDir = Join-Path -Path $PublishPath -ChildPath "$DisplayName.app"
    [string]$ContentsDir = Join-Path $AppDir 'Contents'
    [string]$MacOSDir = Join-Path $ContentsDir 'MacOS'
    [string]$ResourcesDir = Join-Path $ContentsDir 'Resources'

    # Create dirs
    $null = New-Item -ItemType Directory -Force -Path $MacOSDir
    $null = New-Item -ItemType Directory -Force -Path $ResourcesDir
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $ContentsDir 'en.lproj')

    # Info.plist
    @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>$DisplayName</string>
    <key>CFBundleDisplayName</key>
    <string>$DisplayName</string>
    <key>CFBundleIdentifier</key>
    <string>$BundleIdentifier</string>
    <key>CFBundleVersion</key>
    <string>$($VersionInfo.MajorMinorPatch)</string>
    <key>CFBundleShortVersionString</key>
    <string>$($VersionInfo.MajorMinorPatch)</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>launcher</string>
    <key>LSMinimumSystemVersion</key>
    <string>15.0</string>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2026 Werkr. All rights reserved.</string>
    <key>LSBackgroundOnly</key>
    <true/>
</dict>
</plist>
"@ | Set-Content -Path (Join-Path $ContentsDir 'Info.plist') -NoNewline

    # Copy published binaries into MacOS/
    Copy-Item -Path (Join-Path $OutputPath '*') -Destination $MacOSDir -Recurse -Force

    # Create launcher script
    if ($ProductType -ieq 'ServerBundle') {
        # ServerBundle launcher starts both Server and Api
        @"
#!/bin/bash
SCRIPT_DIR="`$(cd "`$(dirname "`$0")" && pwd)"
"`$SCRIPT_DIR/Werkr.Server" &
SERVER_PID=`$!
"`$SCRIPT_DIR/Werkr.Api" &
API_PID=`$!

cleanup() {
    kill `$SERVER_PID `$API_PID 2>/dev/null
    wait `$SERVER_PID `$API_PID 2>/dev/null
}
trap cleanup EXIT INT TERM

wait `$SERVER_PID `$API_PID
"@ | Set-Content -Path (Join-Path $MacOSDir 'launcher') -NoNewline
    }
    else {
        # Single product launcher
        [string]$BinaryName = "Werkr.$ProductType"
        @"
#!/bin/bash
SCRIPT_DIR="`$(cd "`$(dirname "`$0")" && pwd)"
exec "`$SCRIPT_DIR/$BinaryName"
"@ | Set-Content -Path (Join-Path $MacOSDir 'launcher') -NoNewline
    }

    # Make launcher executable
    if ($IsLinux -or $IsMacOS) {
        chmod +x (Join-Path $MacOSDir 'launcher')
    }

    return $Counter + 1
}

function Compress-PublishArtifacts {
<#
    .SYNOPSIS
        Compress portable editions to .zip and .tar.gz, then remove the folders.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [string]$PublishPath,

        [Parameter(Mandatory)]
        [bool]$SkipCompression,

        [Parameter(Mandatory)]
        [bool]$SkipTar
    )

    if ($SkipCompression) { return }

    Write-Host 'Compressing publish artifacts...'
    foreach ($dir in (Get-ChildItem -Path $PublishPath -Directory)) {
        # Skip .app bundles (they get compressed as a directory)
        if ($dir.Name -like '*.app') {
            [hashtable]$ZipParams = @{
                Path            = $dir.FullName
                DestinationPath = "$($dir.FullName).zip"
                Force           = $true
                Verbose         = $Verbose
            }
            Compress-Archive @ZipParams | Out-Null
            Remove-Item -Path $dir.FullName -Recurse -Force -Verbose:$Verbose
            continue
        }

        [hashtable]$ZipParams = @{
            Path            = $dir.FullName
            DestinationPath = "$($dir.FullName).zip"
            Force           = $true
            Verbose         = $Verbose
        }
        Compress-Archive @ZipParams | Out-Null

        if (($IsLinux -or $IsMacOS) -and (-not $SkipTar)) {
            tar -czvf "$($dir.FullName).tar.gz" -C $dir.Parent.FullName $dir.Name | Out-Null
        }
        Remove-Item -Path $dir.FullName -Recurse -Force -Verbose:$Verbose
    }
}

#endregion functions


#region Hard coded values

# Minimum required dotnet SDK major version
[int]$DotNetVersion = 10

#endregion Hard coded values


#region Publish

# Repo root is one level up from scripts/
[string]$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')

Push-Location -Path $RepoRoot
try {
    # Assert prerequisites
    Assert-DotnetInstalled -DotNetVersion $DotNetVersion -Verbose:$Verbose
    Assert-DpkgDebInstalled -BuildDebInstallers $BuildDebInstallers -Verbose:$Verbose
    Assert-TarInstalled -SkipTar $SkipTar -Verbose:$Verbose

    # Obtain version
    [hashtable]$VersionInfo = Get-GitVersion -Verbose:$Verbose
    Write-Host "Build version: $($VersionInfo.SemVer)"

    # Determine matrix
    [string[]]$OperatingSystem = $Platform     -ieq 'All' ? @('windows', 'linux', 'macos') : @($Platform)
    [string[]]$CPUArch         = $Architecture -ieq 'All' ? @('x64', 'arm64')              : @($Architecture)

    # Product types:
    #   ServerBundle = publishes both Werkr.Server + Werkr.Api into a single package
    #   Agent        = publishes Werkr.Agent standalone
    [string[]]$ProductTypes = $Application -ieq 'All' ? @('ServerBundle', 'Agent') : @($Application)

    # Create output directory
    [string]$PublishPath = Join-Path -Path $RepoRoot -ChildPath 'Publish'
    $PublishPath = New-Item -Path $PublishPath -ItemType Directory -Force -Verbose:$Verbose

    [int]$Counter = 1

    foreach ($ProductType in $ProductTypes) {
        # Determine which projects to publish
        [string[]]$ProjectPaths = switch ($ProductType) {
            'ServerBundle' {
                @(
                    (Join-Path $RepoRoot 'src' 'Werkr.Server' 'Werkr.Server.csproj'),
                    (Join-Path $RepoRoot 'src' 'Werkr.Api'    'Werkr.Api.csproj')
                )
            }
            'Agent' {
                @(
                    (Join-Path $RepoRoot 'src' 'Werkr.Agent' 'Werkr.Agent.csproj')
                )
            }
            default {
                throw "Unknown product type: $ProductType"
            }
        }

        foreach ($OS in $OperatingSystem) {
            foreach ($Arch in $CPUArch) {
                [string]$RuntimeIdentifier = switch ($OS) {
                    'windows' { "win-$Arch" }
                    'macos'   { "osx-$Arch" }
                    default   { "$OS-$Arch" }
                }
                [string]$EditionName = "Werkr.$ProductType.$($VersionInfo.SemVer).$RuntimeIdentifier"
                [string]$OutputPath  = Join-Path -Path $PublishPath -ChildPath $EditionName

                if ($OS -eq 'windows') {
                    foreach ($ProjPath in $ProjectPaths) {
                        [hashtable]$GrpcParams = @{
                            ProjectPath       = $ProjPath
                            RuntimeIdentifier = $RuntimeIdentifier
                            Arch              = $Arch
                            Verbose           = $Verbose
                        }
                        Set-GrpcToolsArm64Directory @GrpcParams
                    }
                }

                # Publish all projects for this product type into the same output path
                foreach ($ProjPath in $ProjectPaths) {
                    [hashtable]$ExeParams = @{
                        OutputPath        = $OutputPath
                        ProjectPath       = $ProjPath
                        RuntimeIdentifier = $RuntimeIdentifier
                        VersionInfo       = $VersionInfo
                        Counter           = $Counter
                        Verbose           = $Verbose
                    }
                    $Counter = New-Executable @ExeParams
                }

                # Build installers
                [hashtable]$InstallerParams = @{
                    OS                 = $OS
                    BuildMsiInstallers = $BuildMsiInstallers
                    BuildDebInstallers = $BuildDebInstallers
                    BuildMacOSPackage  = $BuildMacOSPackage
                    ProductType        = $ProductType
                    RuntimeIdentifier  = $RuntimeIdentifier
                    Arch               = $Arch
                    VersionInfo        = $VersionInfo
                    EditionName        = $EditionName
                    OutputPath         = $OutputPath
                    PublishPath        = $PublishPath
                    Counter            = $Counter
                    Verbose            = $Verbose
                }
                $Counter = Build-Installer @InstallerParams
            }
        }
    }

    # Compress portable artifacts
    [hashtable]$CompressionParams = @{
        PublishPath     = $PublishPath
        SkipCompression = $SkipCompression
        SkipTar         = $SkipTar
        Verbose         = $Verbose
    }
    Compress-PublishArtifacts @CompressionParams

    Write-Host "`nPublish complete. Artifacts: $PublishPath" -ForegroundColor Green
} finally {
    Pop-Location
}

#endregion Publish
