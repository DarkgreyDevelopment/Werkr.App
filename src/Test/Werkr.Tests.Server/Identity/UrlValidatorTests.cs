using Werkr.Server.Helpers;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for the <see cref="UrlValidator"/> helper class defined in the <c>Werkr.Server</c> project. Validates
/// that the <see cref="IsLocalUrl"/> method correctly accepts local (relative) URLs and rejects absolute URLs,
/// protocol-relative URLs, and null/empty strings, preventing open redirect vulnerabilities in authentication return
/// URL handling.
/// </summary>
[TestClass]
public class UrlValidatorTests {
    /// <summary>
    /// Verifies that a local relative URL path (e.g., "/agents") is accepted by <see cref="UrlValidator.IsLocalUrl"/>
    /// as a valid return URL.
    /// </summary>
    [TestMethod]
    public void ReturnUrl_LocalUrl_Accepted( ) {
        bool result = UrlValidator.IsLocalUrl( "/agents" );

        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies that an absolute URL with an external domain (e.g., "https://evil.example.com") is rejected by <see
    /// cref="UrlValidator.IsLocalUrl"/>, preventing open redirect attacks.
    /// </summary>
    [TestMethod]
    public void ReturnUrl_AbsoluteUrl_Rejected( ) {
        bool result = UrlValidator.IsLocalUrl( "https://evil.example.com" );

        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that a protocol-relative URL (e.g., "//evil.example.com") is rejected by <see
    /// cref="UrlValidator.IsLocalUrl"/>, preventing scheme-agnostic open redirect attacks.
    /// </summary>
    [TestMethod]
    public void ReturnUrl_ProtocolRelativeUrl_Rejected( ) {
        bool result = UrlValidator.IsLocalUrl( "//evil.example.com" );

        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that <see langword="null"/> and empty string inputs are rejected by <see
    /// cref="UrlValidator.IsLocalUrl"/>, ensuring no redirect occurs for missing return URLs.
    /// </summary>
    [TestMethod]
    public void ReturnUrl_NullOrEmpty_Rejected( ) {
        Assert.IsFalse( UrlValidator.IsLocalUrl( null ) );
        Assert.IsFalse( UrlValidator.IsLocalUrl( string.Empty ) );
    }
}
