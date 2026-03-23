#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies that only platform-specific packages changed in a NuGet lock file.

.DESCRIPTION
    Compares a backup lock file against the current lock file and validates that
    only expected platform-specific Aspire packages differ between them.

    Expected platform-specific packages:
    - Aspire.Dashboard.Sdk.<platform>
    - Aspire.Hosting.Orchestration.<platform>

.PARAMETER BackupPath
    Path to the backup lock file (before regeneration).

.PARAMETER CurrentPath
    Path to the current lock file (after regeneration).

.PARAMETER FromPlatform
    The source platform RID (e.g., linux-x64, linux-arm64). Defaults to linux-x64.

.PARAMETER ToPlatform
    The target platform RID (e.g., linux-x64, linux-arm64). Defaults to linux-arm64.

.EXAMPLE
    ./Test-LockFileChanges.ps1 -BackupPath packages.lock.json.backup -CurrentPath packages.lock.json

.EXAMPLE
    ./Test-LockFileChanges.ps1 -BackupPath packages.lock.json.backup -CurrentPath packages.lock.json -FromPlatform linux-arm64 -ToPlatform linux-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BackupPath,

    [Parameter(Mandatory)]
    [string]$CurrentPath,

    [Parameter()]
    [ValidatePattern('^((linux|win)-(x64|arm64)|osx-arm64)$')]
    [string]$FromPlatform = 'linux-x64',

    [Parameter()]
    [ValidatePattern('^((linux|win)-(x64|arm64)|osx-arm64)$')]
    [string]$ToPlatform = 'linux-arm64'
)

$ErrorActionPreference = 'Stop'

# Validate platform format
Write-Host "Platform transition: $FromPlatform -> $ToPlatform"

# Platform-specific package patterns that are allowed to differ
# These packages have platform-specific variants that will change when switching RIDs
# We allow any valid platform suffix since we support multiple target platforms
# Microsoft.NET.ILLink.Tasks is implicitly added by .NET 10 SDK when PublishSingleFile+SelfContained are enabled
$AllowedPackagePatterns = @(
    '^Aspire\.Dashboard\.Sdk\.(linux|win|osx)-(x64|arm64)$',
    '^Aspire\.Hosting\.Orchestration\.(linux|win|osx)-(x64|arm64)$',
    '^Microsoft\.NET\.ILLink\.Tasks$'
)

function Test-AllowedPackage {
    param([string]$PackageName)
    foreach ($pattern in $AllowedPackagePatterns) {
        if ($PackageName -match $pattern) {
            return $true
        }
    }
    return $false
}

function Test-ProjectDependencyChangesAllowed {
    param(
        [hashtable]$BackupEntry,
        [hashtable]$CurrentEntry
    )

    if (($null -eq $BackupEntry) -or ($null -eq $CurrentEntry)) {
        return $false
    }

    if (($BackupEntry.type -ne 'Project') -or ($CurrentEntry.type -ne 'Project')) {
        return $false
    }

    $backupDeps = @{}
    $currentDeps = @{}

    if ($BackupEntry.ContainsKey('dependencies') -and ($null -ne $BackupEntry.dependencies)) {
        $backupDeps = $BackupEntry.dependencies
    }
    if ($CurrentEntry.ContainsKey('dependencies') -and ($null -ne $CurrentEntry.dependencies)) {
        $currentDeps = $CurrentEntry.dependencies
    }

    # True means: the only differences between the two Project dependency maps are
    # allowed platform-specific packages, and there is at least one such difference.
    $allDependencyNames = @($backupDeps.Keys; $currentDeps.Keys) | Select-Object -Unique
    $foundAllowedDifference = $false

    foreach ($dependencyName in $allDependencyNames) {
        $inBackup = $backupDeps.ContainsKey($dependencyName)
        $inCurrent = $currentDeps.ContainsKey($dependencyName)

        if (($inBackup -and $inCurrent) -and ($backupDeps[$dependencyName] -eq $currentDeps[$dependencyName])) {
            continue
        }

        if (-not (Test-AllowedPackage $dependencyName)) {
            return $false
        }

        $foundAllowedDifference = $true
    }

    return $foundAllowedDifference
}

function Test-LockEntryDifferent {
    param(
        [hashtable]$BackupEntry,
        [hashtable]$CurrentEntry
    )

    if (($null -eq $BackupEntry) -or ($null -eq $CurrentEntry)) {
        return $true
    }

    if (($BackupEntry.type -eq 'Transitive') -and ($CurrentEntry.type -eq 'Transitive')) {
        return $BackupEntry.contentHash -ne $CurrentEntry.contentHash
    }

    if (($BackupEntry.type -eq 'Project') -and ($CurrentEntry.type -eq 'Project')) {
        $backupDeps = @{}
        $currentDeps = @{}

        if ($BackupEntry.ContainsKey('dependencies') -and ($null -ne $BackupEntry.dependencies)) {
            $backupDeps = $BackupEntry.dependencies
        }
        if ($CurrentEntry.ContainsKey('dependencies') -and ($null -ne $CurrentEntry.dependencies)) {
            $currentDeps = $CurrentEntry.dependencies
        }

        $allDependencyNames = @($backupDeps.Keys; $currentDeps.Keys) | Select-Object -Unique
        foreach ($dependencyName in $allDependencyNames) {
            $inBackup = $backupDeps.ContainsKey($dependencyName)
            $inCurrent = $currentDeps.ContainsKey($dependencyName)

            if (
                ($inBackup -and $inCurrent) -and
                ($backupDeps[$dependencyName] -eq $currentDeps[$dependencyName])
            ) {
                continue
            }

            return $true
        }

        return $false
    }

    $backupJson = $BackupEntry | ConvertTo-Json -Compress
    $currentJson = $CurrentEntry | ConvertTo-Json -Compress
    return $backupJson -ne $currentJson
}

# Read and parse both lock files
$backup = Get-Content $BackupPath -Raw | ConvertFrom-Json -AsHashtable
$current = Get-Content $CurrentPath -Raw | ConvertFrom-Json -AsHashtable

$unexpectedChanges = @()
$expectedChanges = @()

function Test-PlatformFramework {
    param([string]$Framework)
    # Framework entries like "net10.0/linux-x64" or "net10.0/win-arm64" are platform-specific
    return $Framework -match '^net\d+\.\d+/(linux|win|osx)-(x64|x86|arm64|arm)$'
}

# Compare each target framework
foreach ($framework in $current.dependencies.Keys) {
    $backupDeps = $backup.dependencies[$framework]
    $currentDeps = $current.dependencies[$framework]

    if ($null -eq $backupDeps) {
        if (-not (Test-PlatformFramework $framework)) {
            $unexpectedChanges += "New framework added: $framework"
        }
        continue
    }

    # Find all unique package names across both
    $allPackages = @($backupDeps.Keys) + @($currentDeps.Keys) | Select-Object -Unique

    foreach ($package in $allPackages) {
        $inBackup = $backupDeps.ContainsKey($package)
        $inCurrent = $currentDeps.ContainsKey($package)

        if ($inBackup -and $inCurrent) {
            # Package exists in both - check if it changed
            $backupEntry = $backupDeps[$package]
            $currentEntry = $currentDeps[$package]

            if (Test-LockEntryDifferent -BackupEntry $backupEntry -CurrentEntry $currentEntry) {
                if (Test-AllowedPackage $package) {
                    $expectedChanges += [PSCustomObject]@{
                        Package   = $package
                        Type      = 'Modified'
                        Framework = $framework
                    }
                } elseif (Test-ProjectDependencyChangesAllowed -BackupEntry $backupEntry -CurrentEntry $currentEntry) {
                    $expectedChanges += [PSCustomObject]@{
                        Package   = $package
                        Type      = 'Modified'
                        Framework = $framework
                    }
                } else {
                    $unexpectedChanges += "Package modified: $package in $framework"
                }
            }
        } elseif ($inBackup -and -not $inCurrent) {
            # Package removed
            if (Test-AllowedPackage $package) {
                $expectedChanges += [PSCustomObject]@{
                    Package   = $package
                    Type      = 'Removed'
                    Framework = $framework
                }
            } else {
                $unexpectedChanges += "Package removed: $package from $framework"
            }
        } elseif (-not $inBackup -and $inCurrent) {
            # Package added
            if (Test-AllowedPackage $package) {
                $expectedChanges += [PSCustomObject]@{
                    Package   = $package
                    Type      = 'Added'
                    Framework = $framework
                }
            } else {
                $unexpectedChanges += "Package added: $package to $framework"
            }
        }
    }
}

# Check for removed frameworks
foreach ($framework in $backup.dependencies.Keys) {
    if (-not $current.dependencies.ContainsKey($framework)) {
        if (-not (Test-PlatformFramework $framework)) {
            $unexpectedChanges += "Framework removed: $framework"
        }
    }
}

# Report results
if ($expectedChanges.Count -gt 0) {
    Write-Host 'Platform-specific package changes:'
    foreach ($change in $expectedChanges) {
        $versionInfo = [string]::Empty
        if ($change.Type -eq 'Modified') {
            $backupEntry = $backup.dependencies[$change.Framework][$change.Package]
            $currentEntry = $current.dependencies[$change.Framework][$change.Package]
            if ($null -ne $backupEntry -and $null -ne $currentEntry -and $backupEntry.ContainsKey('resolved') -and $currentEntry.ContainsKey('resolved')) {
                $backupVersion = $backupEntry.resolved
                $currentVersion = $currentEntry.resolved
                $versionInfo = " (version: $backupVersion -> $currentVersion)"
            }
        } elseif ($change.Type -eq 'Removed') {
            $backupEntry = $backup.dependencies[$change.Framework][$change.Package]
            if ($null -ne $backupEntry -and $backupEntry.ContainsKey('resolved')) {
                $backupVersion = $backupEntry.resolved
                $versionInfo = " (version: $backupVersion)"
            }
        } elseif ($change.Type -eq 'Added') {
            $currentEntry = $current.dependencies[$change.Framework][$change.Package]
            if ($null -ne $currentEntry -and $currentEntry.ContainsKey('resolved')) {
                $currentVersion = $currentEntry.resolved
                $versionInfo = " (version: $currentVersion)"
            }
        }
        Write-Host "  [$($change.Type)] $($change.Package)$versionInfo"
    }
    Write-Host [string]::Empty
}

if ($unexpectedChanges.Count -gt 0) {
    Write-Error (
        'ERROR: Non-platform-specific packages changed in lock file! ' +
        'Only platform-specific Aspire packages (and the project entries that reference them) should differ between platforms.' +
        "`nUnexpected changes detected:" + ($unexpectedChanges | ForEach-Object { "`n  - $_" })
    )
    exit 1
}

if ($expectedChanges.Count -eq 0) {
    Write-Host 'No changes detected in lock file'
} else {
    Write-Host 'Lock file updated successfully - only platform-specific packages changed'
}

exit 0
