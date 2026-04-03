#Requires -Version 7.6
<#
    .SYNOPSIS
        Build Werkr Docker images.
    .DESCRIPTION
        Supports two build modes:
          source (default) — builds from source inside Docker
          deb              — publishes .deb packages first, then builds lightweight images

        TLS certificates are generated automatically on first run when the certs/
        directory does not exist. Use -SkipCertGeneration to opt out (e.g. when
        supplying your own certificates) or -GenerateCerts to force regeneration.

        Two certificates are generated:
          - Control plane (Server + API): certs/werkr-server.pfx
          - Agent:                        certs/werkr-agent.pfx
          - Shared CA:                    certs/werkr-ca.pem

    .EXAMPLE
        ./scripts/docker-build.ps1                      # source build all (generates certs on first run)
        ./scripts/docker-build.ps1 -Target server       # source build server only
        ./scripts/docker-build.ps1 -Deb                 # publish .deb then build all
        ./scripts/docker-build.ps1 -Deb -Push           # publish, build, and push all
        ./scripts/docker-build.ps1 -SkipCertGeneration  # build without generating certs
        ./scripts/docker-build.ps1 -GenerateCerts       # force-regenerate certs then build
        ./scripts/docker-build.ps1 -TrustCA             # trust the dev CA in the OS trust store
#>
[CmdletBinding()]
param (
    [ValidateSet('all', 'server', 'api', 'agent')]
    [string]$Target = 'all',

    [string]$Registry = ($env:DOCKER_REGISTRY ?? 'ghcr.io/werkr'),

    [string]$Tag = ($env:DOCKER_TAG ?? 'latest'),

    [switch]$Push,

    [switch]$Deb,

    [Parameter(HelpMessage = 'Skip TLS certificate generation (use your own certs).')]
    [switch]$SkipCertGeneration,

    [Parameter(HelpMessage = 'Force-regenerate TLS certificates even if they already exist.')]
    [switch]$GenerateCerts,

    [Parameter(HelpMessage = 'Trust the Werkr dev CA in the OS certificate trust store. Requires elevation on Windows/macOS.')]
    [switch]$TrustCA
)
$ErrorActionPreference = 'Stop'

[string]$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
[string]$BuildMode = $Deb ? 'deb' : 'source'
[string]$CertsDir = Join-Path $RepoRoot 'certs'

#region Certificate Generation

function Assert-OpenSslInstalled {
<#
    .SYNOPSIS
        Assert that openssl is available on the PATH.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param ()

    [string]$OpenSsl = Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ([string]::IsNullOrWhiteSpace($OpenSsl)) {
        throw 'openssl is not installed or not on PATH. Install OpenSSL to generate TLS certificates.'
    }
    Write-Verbose "openssl found at: $OpenSsl"
}

function New-WerkrCertificates {
<#
    .SYNOPSIS
        Generate a local dev CA and per-service TLS certificates for Docker.
    .DESCRIPTION
        Creates:
          - A self-signed CA (werkr-ca.pem, werkr-ca-key.pem)
          - A control-plane cert for Server + API (werkr-server.pfx)
            SANs: localhost, werkr-api, werkr-server
          - An agent cert (werkr-agent.pfx)
            SANs: localhost, werkr-agent
        All files are written to the certs/ directory at the repo root.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)]
        [string]$OutputDir
    )

    Assert-OpenSslInstalled

    [string]$CaKey   = Join-Path $OutputDir 'werkr-ca-key.pem'
    [string]$CaCert  = Join-Path $OutputDir 'werkr-ca.pem'
    [string]$PfxPass = 'werkr-dev'

    $null = New-Item -ItemType Directory -Path $OutputDir -Force

    Write-Host '==> Generating Werkr dev CA...'

    # Generate CA private key
    & openssl genpkey -algorithm RSA -out $CaKey -pkeyopt rsa_keygen_bits:4096 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to generate CA private key' }

    # Generate CA certificate (10-year validity)
    & openssl req -x509 -new -nodes -key $CaKey -sha256 -days 3650 -subj '/CN=Werkr Dev CA/O=Werkr/OU=Development' -out $CaCert 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to generate CA certificate' }

    # --- Control plane cert (Server + API) ---
    Write-Host '==> Generating control plane certificate (Server + API)...'
    $ServerCertParams = @{
        Name        = 'werkr-server'
        SANs        = @('localhost', 'werkr-api', 'werkr-server')
        CaKey       = $CaKey
        CaCert      = $CaCert
        OutputDir   = $OutputDir
        PfxPassword = $PfxPass
    }
    New-ServiceCertificate @ServerCertParams

    # --- Agent cert ---
    Write-Host '==> Generating agent certificate...'
    $AgentCertParams = @{
        Name        = 'werkr-agent'
        SANs        = @('localhost', 'werkr-agent')
        CaKey       = $CaKey
        CaCert      = $CaCert
        OutputDir   = $OutputDir
        PfxPassword = $PfxPass
    }
    New-ServiceCertificate @AgentCertParams

    Write-Host "==> Certificates generated in $OutputDir" -ForegroundColor Green
    Write-Host "    CA:      werkr-ca.pem"
    Write-Host "    Server:  werkr-server.pfx  (password: $PfxPass)"
    Write-Host "    Agent:   werkr-agent.pfx   (password: $PfxPass)"
    Write-Host ''
    Write-Host 'NOTE: Your browser will not trust these certificates by default.' -ForegroundColor Yellow
    Write-Host 'To enable interactive Blazor features, trust the CA in your OS:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  macOS:   sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain certs/werkr-ca.pem' -ForegroundColor Cyan
    Write-Host '  Windows: Import-Certificate -FilePath certs\werkr-ca.pem -CertStoreLocation Cert:\LocalMachine\Root' -ForegroundColor Cyan
    Write-Host '  Linux:   sudo cp certs/werkr-ca.pem /usr/local/share/ca-certificates/werkr-ca.crt && sudo update-ca-certificates' -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'Or re-run this script with -TrustCA to do it automatically.' -ForegroundColor Yellow
}

function New-ServiceCertificate {
<#
    .SYNOPSIS
        Generate a TLS certificate signed by the Werkr dev CA.
#>
    [CmdletBinding()]
    [OutputType([System.Void])]
    param (
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$SANs,
        [Parameter(Mandatory)][string]$CaKey,
        [Parameter(Mandatory)][string]$CaCert,
        [Parameter(Mandatory)][string]$OutputDir,
        [Parameter(Mandatory)][string]$PfxPassword
    )

    [string]$KeyFile  = Join-Path $OutputDir "$Name-key.pem"
    [string]$CsrFile  = Join-Path $OutputDir "$Name.csr"
    [string]$CertFile = Join-Path $OutputDir "$Name.pem"
    [string]$PfxFile  = Join-Path $OutputDir "$Name.pfx"
    [string]$ExtFile  = Join-Path $OutputDir "$Name-ext.cnf"

    # Build SAN extension config
    [string[]]$DnsEntries = @()
    for ([int]$i = 0; $i -lt $SANs.Count; $i++) {
        $DnsEntries += "DNS.$($i + 1) = $($SANs[$i])"
    }
    [string]$ExtContent = @"
authorityKeyIdentifier=keyid,issuer
basicConstraints=CA:FALSE
keyUsage = digitalSignature, nonRepudiation, keyEncipherment, dataEncipherment
extendedKeyUsage = serverAuth
subjectAltName = @alt_names

[alt_names]
$($DnsEntries -join "`n")
"@
    Set-Content -Path $ExtFile -Value $ExtContent -NoNewline

    # Generate service private key
    & openssl genpkey -algorithm RSA -out $KeyFile -pkeyopt rsa_keygen_bits:2048 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to generate private key for $Name" }

    # Generate CSR
    & openssl req -new -key $KeyFile -out $CsrFile -subj "/CN=$($SANs[0])/O=Werkr/OU=$Name" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to generate CSR for $Name" }

    # Sign with CA (2-year validity)
    & openssl x509 -req -in $CsrFile -CA $CaCert -CAkey $CaKey -CAcreateserial -out $CertFile -days 730 -sha256 -extfile $ExtFile 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to sign certificate for $Name" }

    # Export to PFX
    & openssl pkcs12 -export -out $PfxFile -inkey $KeyFile -in $CertFile -certfile $CaCert -password "pass:$PfxPassword" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to export PFX for $Name" }

    # Clean up intermediate files
    Remove-Item -Path $CsrFile, $ExtFile -Force -ErrorAction SilentlyContinue
}

#endregion Certificate Generation

# --- Certificate generation (default on first run) ---
if ($GenerateCerts) {
    New-WerkrCertificates -OutputDir $CertsDir
} elseif (-not $SkipCertGeneration -and -not (Test-Path $CertsDir)) {
    Write-Host '==> No certs/ directory found. Generating TLS certificates for Docker...'
    New-WerkrCertificates -OutputDir $CertsDir
} elseif (Test-Path $CertsDir) {
    Write-Verbose 'certs/ directory exists, skipping certificate generation.'
}

# --- Trust CA in OS trust store ---
if ($TrustCA) {
    [string]$CaPem = Join-Path $CertsDir 'werkr-ca.pem'
    if (-not (Test-Path $CaPem)) {
        throw "CA certificate not found at $CaPem. Run with -GenerateCerts first."
    }

    if ($IsWindows) {
        Write-Host '==> Trusting Werkr dev CA in Windows certificate store (requires elevation)...'
        Import-Certificate -FilePath $CaPem -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    } elseif ($IsMacOS) {
        Write-Host '==> Trusting Werkr dev CA in macOS System Keychain (requires sudo)...'
        & sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain $CaPem
        if ($LASTEXITCODE -ne 0) { throw 'Failed to trust CA on macOS.' }
    } elseif ($IsLinux) {
        Write-Host '==> Trusting Werkr dev CA in Linux CA store (requires sudo)...'
        & sudo cp $CaPem /usr/local/share/ca-certificates/werkr-ca.crt
        & sudo update-ca-certificates
        if ($LASTEXITCODE -ne 0) { throw 'Failed to trust CA on Linux.' }
    } else {
        Write-Warning 'Unknown OS — cannot auto-trust. Please trust certs/werkr-ca.pem manually.'
    }
    Write-Host '==> CA trusted successfully.' -ForegroundColor Green
}

# If .deb mode, run publish.ps1 first to produce the .deb packages
if ($Deb) {
    Write-Host '==> Publishing .deb packages via publish.ps1...'
    $DebParams = @{
        Application        = 'All'
        Platform           = 'linux'
        Architecture       = 'x64'
        BuildDebInstallers = $true
        SkipCompression    = $true
    }
    & pwsh (Join-Path $PSScriptRoot 'publish.ps1') @DebParams
    if ($LASTEXITCODE -ne 0) { throw 'publish.ps1 failed' }
    Write-Host '==> .deb packages ready in Publish/'
}

function Build-Image {
    param (
        [string]$Name,
        [string]$Dockerfile
    )
    [string]$Image = "$Registry/werkr-${Name}:$Tag"
    Write-Host "==> Building $Image (mode: $BuildMode)"
    & docker build -t $Image -f (Join-Path $RepoRoot $Dockerfile) --platform linux/amd64 --build-arg "BUILD_MODE=$BuildMode" $RepoRoot
    if ($LASTEXITCODE -ne 0) { throw "Docker build failed for $Name" }
    if ($Push) {
        Write-Host "==> Pushing $Image"
        & docker push $Image
        if ($LASTEXITCODE -ne 0) { throw "Docker push failed for $Name" }
    }
}

$Images = @{
    server = 'src/Werkr.Server/Dockerfile'
    api    = 'src/Werkr.Api/Dockerfile'
    agent  = 'src/Werkr.Agent/Dockerfile'
}

if ($Target -eq 'all') {
    foreach ($entry in $Images.GetEnumerator()) {
        Build-Image -Name $entry.Key -Dockerfile $entry.Value
    }
} else {
    Build-Image -Name $Target -Dockerfile $Images[$Target]
}

Write-Host 'Done.' -ForegroundColor Green
