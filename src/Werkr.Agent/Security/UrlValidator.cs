using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Werkr.Common.Models;
using Werkr.Core.Security;

namespace Werkr.Agent.Security;

/// <summary>
/// Validates URLs for network actions with SSRF protection, private IP rejection,
/// DNS-pinning verification, and prefix-based allowlist enforcement.
/// Uses <see cref="IOptionsMonitor{T}"/> for hot-reload support.
/// </summary>
public sealed class UrlValidator : IUrlValidator {

    private readonly IOptionsMonitor<ActionOperatorConfiguration> _options;
    private readonly ILogger<UrlValidator> _logger;

    /// <summary>Creates a new <see cref="UrlValidator"/>.</summary>
    public UrlValidator(
        IOptionsMonitor<ActionOperatorConfiguration> options,
        ILogger<UrlValidator> logger
    ) {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Uri ValidateUrl( string url ) {
        ActionOperatorConfiguration config = _options.CurrentValue;

        // 1. Check EnableNetworkActions gate
        if (!config.EnableNetworkActions) {
            _logger.LogWarning( "Network actions are disabled. Rejecting URL '{Url}'.", url );
            throw new UnauthorizedAccessException(
                "Network actions are disabled. Set 'ActionOperator:EnableNetworkActions' to true to enable." );
        }

        // 2. Parse URL
        if (!Uri.TryCreate( url, UriKind.Absolute, out Uri? uri )) {
            throw new UnauthorizedAccessException( $"Invalid URL: '{url}' — must be an absolute URI." );
        }

        // 3. Validate scheme
        if (!string.Equals( uri.Scheme, "http", StringComparison.OrdinalIgnoreCase )
            && !string.Equals( uri.Scheme, "https", StringComparison.OrdinalIgnoreCase )) {
            throw new UnauthorizedAccessException(
                $"URL scheme '{uri.Scheme}' is not allowed. Only 'http' and 'https' are permitted." );
        }

        // 4. Check AllowedUrls prefix list
        if (config.AllowedUrls.Length > 0) {
            bool allowed = false;
            foreach (string prefix in config.AllowedUrls) {
                if (url.StartsWith( prefix, StringComparison.OrdinalIgnoreCase )) {
                    allowed = true;
                    break;
                }
            }
            if (!allowed) {
                string prefixes = string.Join( ", ", config.AllowedUrls );
                _logger.LogWarning(
                    "URL '{Url}' does not match any allowed URL prefix [{Prefixes}].", url, prefixes );
                throw new UnauthorizedAccessException(
                    $"URL '{url}' is not in the allowed URL list. Allowed prefixes: [{prefixes}]" );
            }
        }

        // 5. Resolve DNS and validate resolved IPs
        if (!config.AllowPrivateNetworks) {
            ValidateResolvedAddresses( uri );
        }

        return uri;
    }

    /// <summary>
    /// Resolves the hostname and validates that none of the resolved IP addresses
    /// are private, loopback, or link-local.
    /// </summary>
    /// <remarks>
    /// This pre-request DNS check provides clear error messages before the HTTP request is
    /// attempted. A second layer of defense exists via the <c>SocketsHttpHandler.ConnectCallback</c>
    /// registered on the <c>WerkrActions</c> named client, which validates the resolved IP at
    /// actual TCP connect time — closing the DNS TOCTOU / rebinding window.
    /// </remarks>
    private void ValidateResolvedAddresses( Uri uri ) {
        IPAddress[] addresses;
        try {
            addresses = Dns.GetHostAddresses( uri.Host );
        } catch (SocketException ex) {
            throw new UnauthorizedAccessException(
                $"DNS resolution failed for host '{uri.Host}': {ex.Message}", ex );
        }

        if (addresses.Length == 0) {
            throw new UnauthorizedAccessException(
                $"DNS resolution returned no addresses for host '{uri.Host}'." );
        }

        foreach (IPAddress address in addresses) {
            if (IsPrivateOrReserved( address )) {
                _logger.LogWarning(
                    "URL '{Url}' resolves to private/reserved IP {Address}. Rejecting.", uri, address );
                throw new UnauthorizedAccessException(
                    $"URL '{uri}' resolves to a private or reserved IP address ({address}). " +
                    $"Set 'ActionOperator:AllowPrivateNetworks' to true to allow." );
            }
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the address is private (RFC 1918),
    /// loopback (127.x, ::1), or link-local (169.254.x, fe80::).
    /// </summary>
    public static bool IsPrivateOrReserved( IPAddress address ) {
        // Map IPv6-mapped IPv4 to IPv4
        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4( );
        }

        // Loopback: 127.0.0.0/8, ::1
        if (IPAddress.IsLoopback( address )) {
            return true;
        }

        byte[] bytes = address.GetAddressBytes( );

        if (address.AddressFamily == AddressFamily.InterNetwork) {
            // 10.0.0.0/8
            if (bytes[0] == 10) {
                return true;
            }
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) {
                return true;
            }
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) {
                return true;
            }
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) {
                return true;
            }
            // 100.64.0.0/10 (carrier-grade NAT, RFC 6598)
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) {
                return true;
            }
            // 0.0.0.0/8 (current network)
            if (bytes[0] == 0) {
                return true;
            }
        } else if (address.AddressFamily == AddressFamily.InterNetworkV6) {
            // fe80::/10 (link-local)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) {
                return true;
            }
            // fc00::/7 (unique local)
            if ((bytes[0] & 0xFE) == 0xFC) {
                return true;
            }
            // ::1 already handled by IsLoopback, but also :: (unspecified)
            if (address.Equals( IPAddress.IPv6None )) {
                return true;
            }
        }

        return false;
    }
}
