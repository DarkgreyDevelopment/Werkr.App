namespace Werkr.Core.Security;

/// <summary>
/// Validates URLs for use by network actions, providing SSRF protection and
/// allowlist enforcement. Analogous to <see cref="IFilePathResolver"/> for file paths.
/// Throws <see cref="UnauthorizedAccessException"/> if the URL is not allowed.
/// </summary>
public interface IUrlValidator {

    /// <summary>
    /// Validates a URL for use by network actions.
    /// Checks scheme (http/https only), the <c>EnableNetworkActions</c> gate,
    /// the <c>AllowedUrls</c> prefix allowlist, and rejects private/loopback
    /// IP addresses unless <c>AllowPrivateNetworks</c> is enabled.
    /// </summary>
    /// <param name="url">The URL string to validate.</param>
    /// <returns>The validated <see cref="Uri"/> instance.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the URL fails validation (network actions disabled, disallowed URL,
    /// private IP, invalid scheme, etc.).
    /// </exception>
    Uri ValidateUrl( string url );
}
