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
          - macOS    : .pkg installer with launchd service

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

function Assert-PkgBuildInstalled {
<#
    .SYNOPSIS
        Assert that pkgbuild and productbuild are available when macOS .pkg
        installers are requested.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [bool]$BuildMacOSPackage
    )

    if (-not $BuildMacOSPackage) { return }

    foreach ($tool in @('pkgbuild', 'productbuild')) {
        [string]$ToolPath = Get-Command $tool -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
        if ([string]::IsNullOrWhiteSpace($ToolPath)) {
            throw "$tool is not installed. Install Xcode Command Line Tools: xcode-select --install"
        }
        Write-Verbose "$tool found at $ToolPath"
    }
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
                $PkgInstallerParams = @{
                    ProductType = $ProductType
                    VersionInfo = $VersionInfo
                    EditionName = $EditionName
                    OutputPath  = $OutputPath
                    PublishPath = $PublishPath
                    Arch        = $Arch
                    Counter     = $Counter
                    Verbose     = $Verbose
                }
                $Counter = New-PkgInstaller @PkgInstallerParams
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
        Create a .deb package from static template files in src/Installer/Deb/.
        Substitutes build-time values (version, architecture) into the control
        template and stages published binaries.
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
    [string]$DebArch = $RuntimeIdentifier -match 'arm64' ? 'arm64' : 'amd64'

    # Resolve the template directory relative to the repo root
    [string]$RepoRoot = Split-Path -Parent $PSScriptRoot
    [string]$DebInstallerRoot = Join-Path $RepoRoot 'src/Installer/Deb'
    [string]$BuildScript = Join-Path $DebInstallerRoot 'build-deb.ps1'

    if (-not (Test-Path $BuildScript)) {
        throw "build-deb.ps1 not found at $BuildScript. Ensure src/Installer/Deb/ is intact."
    }

    & $BuildScript `
        -ProductType $ProductType `
        -BinaryPath $OutputPath `
        -Version $VersionInfo.MajorMinorPatch `
        -Architecture $DebArch `
        -OutputPath $PublishPath `
        -EditionName $EditionName

    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "build-deb.ps1 failed for $EditionName (exit $LASTEXITCODE)"
    }

    return $Counter + 1
}

function New-PkgInstaller {
<#
    .SYNOPSIS
        Create a macOS .pkg installer by delegating to
        src/Installer/Pkg/build-pkg.ps1.
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
        [string]$Arch,

        [Parameter(Mandatory)]
        [int]$Counter
    )

    Write-Host "[$Counter] Building macOS .pkg: $EditionName"

    # Resolve the build script relative to the repo root
    [string]$RepoRoot = Split-Path -Parent $PSScriptRoot
    [string]$PkgInstallerRoot = Join-Path $RepoRoot 'src/Installer/Pkg'
    [string]$BuildScript = Join-Path $PkgInstallerRoot 'build-pkg.ps1'

    if (-not (Test-Path $BuildScript)) {
        throw "build-pkg.ps1 not found at $BuildScript. Ensure src/Installer/Pkg/ is intact."
    }

    & $BuildScript `
        -ProductType $ProductType `
        -BinaryPath $OutputPath `
        -Version $VersionInfo.MajorMinorPatch `
        -Architecture $Arch `
        -OutputPath $PublishPath `
        -EditionName $EditionName

    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        throw "build-pkg.ps1 failed for $EditionName (exit $LASTEXITCODE)"
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
    Assert-PkgBuildInstalled -BuildMacOSPackage $BuildMacOSPackage -Verbose:$Verbose
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
