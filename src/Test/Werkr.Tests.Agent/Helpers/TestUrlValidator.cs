using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Provides pre-built <see cref="IUrlValidator"/> instances for unit tests.
/// </summary>
internal static class TestUrlValidator {

    /// <summary>
    /// A validator that allows all URLs, returning a parsed <see cref="Uri"/>
    /// without any network-actions gate, allowlist, or SSRF checks.
    /// </summary>
    public static IUrlValidator AllowAll { get; } = new AllowAllUrlValidator( );

    /// <summary>
    /// A validator that rejects all URLs with <see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static IUrlValidator DenyAll { get; } = new DenyAllUrlValidator( );

    private sealed class AllowAllUrlValidator : IUrlValidator {
        public Uri ValidateUrl( string url ) {
            if (!Uri.TryCreate( url, UriKind.Absolute, out Uri? uri )) {
                throw new UnauthorizedAccessException( $"Invalid URL: '{url}'" );
            }
            return uri;
        }
    }

    private sealed class DenyAllUrlValidator : IUrlValidator {
        public Uri ValidateUrl( string url ) =>
            throw new UnauthorizedAccessException( $"URL validation denied (test): '{url}'" );
    }
}
