namespace Werkr.Server.Helpers;

/// <summary>
/// Validates return URLs to prevent open redirect vulnerabilities.
/// </summary>
public static class UrlValidator {
    /// <summary>
    /// Returns true only for local relative URLs.
    /// </summary>
    /// <param name="url">The candidate URL.</param>
    /// <returns>True when URL is safe and local.</returns>
    public static bool IsLocalUrl( string? url ) {
        if (string.IsNullOrWhiteSpace( url )) {
            return false;
        }

        if (!Uri.IsWellFormedUriString( url, UriKind.Relative )) {
            return false;
        }

        if (url.StartsWith( "//", StringComparison.Ordinal )) {
            return false;
        }

        int firstSlash = url.IndexOf( '/' );
        int colonIndex = url.IndexOf( ':' );
        return colonIndex < 0 || (firstSlash >= 0 && colonIndex >= firstSlash);
    }
}
