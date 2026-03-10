using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Agent.Security;
using Werkr.Common.Models;

namespace Werkr.Tests.Agent.Security;

/// <summary>
/// Unit tests for the <see cref="UrlValidator"/> — validates SSRF protection,
/// private IP rejection, scheme enforcement, allowlist gating, and the
/// <c>EnableNetworkActions</c> configuration gate.
/// </summary>
[TestClass]
public class UrlValidatorTests {

    /// <summary>MSTest context.</summary>
    public TestContext TestContext { get; set; } = null!;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static UrlValidator CreateValidator( ActionOperatorConfiguration config ) {
        TestOptionsMonitor<ActionOperatorConfiguration> monitor = new( config );
        return new UrlValidator( monitor, NullLogger<UrlValidator>.Instance );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── EnableNetworkActions Gate ─────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>When disabled, all URLs are rejected.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void Disabled_RejectsAllUrls( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = false,
        } );

        UnauthorizedAccessException ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidateUrl( "https://example.com" ) );
        Assert.Contains( "disabled", ex.Message );
    }

    /// <summary>When enabled, a valid public URL is accepted.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void Enabled_AcceptsPublicUrl( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true, // avoid DNS for unit test
        } );

        Uri result = validator.ValidateUrl( "https://example.com/api" );
        Assert.AreEqual( "https://example.com/api", result.OriginalString );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Scheme Enforcement ───────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>FTP scheme is rejected.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void FtpScheme_Rejected( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
        } );

        UnauthorizedAccessException ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidateUrl( "ftp://example.com/file.txt" ) );
        Assert.Contains( "scheme", ex.Message );
    }

    /// <summary>File scheme is rejected.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void FileScheme_Rejected( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
        } );

        UnauthorizedAccessException ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidateUrl( "file:///etc/passwd" ) );
        Assert.Contains( "scheme", ex.Message );
    }

    /// <summary>Relative URL is rejected.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void RelativeUrl_Rejected( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
        } );

        UnauthorizedAccessException ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidateUrl( "/api/data" ) );
        Assert.Contains( "absolute", ex.Message );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── AllowedUrls Prefix Allowlist ─────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>URL matching an allowed prefix is accepted.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void AllowedPrefix_Accepted( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
            AllowedUrls = ["https://api.example.com/"],
        } );

        Uri result = validator.ValidateUrl( "https://api.example.com/v1/data" );
        Assert.IsNotNull( result );
    }

    /// <summary>URL not matching any allowed prefix is rejected.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void NonMatchingPrefix_Rejected( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
            AllowedUrls = ["https://api.example.com/"],
        } );

        UnauthorizedAccessException ex = Assert.ThrowsExactly<UnauthorizedAccessException>(
            ( ) => validator.ValidateUrl( "https://evil.com/attack" ) );
        Assert.Contains( "allowed URL list", ex.Message );
    }

    /// <summary>Empty AllowedUrls means all URLs are allowed (when enabled).</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void EmptyAllowedUrls_AllAllowed( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
            AllowedUrls = [],
        } );

        Uri result = validator.ValidateUrl( "https://anything.example.com/path" );
        Assert.IsNotNull( result );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Private IP Rejection (SSRF Protection) ───────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>10.x.x.x is private.</summary>
    [TestMethod]
    public void IsPrivate_10Network( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "10.0.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "10.255.255.255" ) ) );
    }

    /// <summary>172.16-31.x.x is private.</summary>
    [TestMethod]
    public void IsPrivate_172Network( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "172.16.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "172.31.255.255" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "172.15.0.1" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "172.32.0.1" ) ) );
    }

    /// <summary>192.168.x.x is private.</summary>
    [TestMethod]
    public void IsPrivate_192Network( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "192.168.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "192.168.255.255" ) ) );
    }

    /// <summary>127.x.x.x is loopback.</summary>
    [TestMethod]
    public void IsPrivate_Loopback( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "127.0.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "127.255.255.255" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Loopback ) );
    }

    /// <summary>::1 IPv6 loopback.</summary>
    [TestMethod]
    public void IsPrivate_IPv6Loopback( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.IPv6Loopback ) );
    }

    /// <summary>169.254.x.x is link-local.</summary>
    [TestMethod]
    public void IsPrivate_LinkLocal( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "169.254.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "169.254.255.255" ) ) );
    }

    /// <summary>100.64.0.0/10 is carrier-grade NAT (RFC 6598).</summary>
    [TestMethod]
    public void IsPrivate_CarrierGradeNat( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "100.64.0.1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "100.127.255.255" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "100.63.255.255" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "100.128.0.0" ) ) );
    }

    /// <summary>fe80:: is IPv6 link-local.</summary>
    [TestMethod]
    public void IsPrivate_IPv6LinkLocal( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "fe80::1" ) ) );
    }

    /// <summary>fc00::/fd00:: is IPv6 unique local.</summary>
    [TestMethod]
    public void IsPrivate_IPv6UniqueLocal( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "fc00::1" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "fd00::1" ) ) );
    }

    /// <summary>0.0.0.0/8 is current-network.</summary>
    [TestMethod]
    public void IsPrivate_ZeroNetwork( ) {
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "0.0.0.0" ) ) );
        Assert.IsTrue( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "0.1.2.3" ) ) );
    }

    /// <summary>Public IPs are not private.</summary>
    [TestMethod]
    public void IsNotPrivate_PublicIPs( ) {
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "8.8.8.8" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "93.184.215.14" ) ) );
        Assert.IsFalse( UrlValidator.IsPrivateOrReserved( IPAddress.Parse( "1.1.1.1" ) ) );
    }

    /// <summary>AllowPrivateNetworks bypasses private IP check.</summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public void AllowPrivateNetworks_BypassesCheck( ) {
        UrlValidator validator = CreateValidator( new ActionOperatorConfiguration {
            EnableNetworkActions = true,
            AllowPrivateNetworks = true,
        } );

        // localhost resolves to 127.0.0.1 — should be allowed when AllowPrivateNetworks is true
        Uri result = validator.ValidateUrl( "http://localhost:8080/api" );
        Assert.IsNotNull( result );
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ── Internal ─────────────────────────────────────────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simple <see cref="IOptionsMonitor{T}"/> implementation for tests.
    /// </summary>
    /// <remarks>Initializes a new instance.</remarks>
    private sealed class TestOptionsMonitor<T>( T currentValue ) : IOptionsMonitor<T> {

        /// <summary>Gets the current options value.</summary>
        public T CurrentValue { get; } = currentValue;

        /// <summary>Returns the current value.</summary>
        public T Get( string? name ) => CurrentValue;

        /// <summary>No-op change listener.</summary>
        public IDisposable? OnChange( Action<T, string?> listener ) => null;
    }
}
