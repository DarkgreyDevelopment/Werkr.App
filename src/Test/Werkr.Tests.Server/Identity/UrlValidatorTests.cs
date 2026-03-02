using Werkr.Server.Helpers;

namespace Werkr.Tests.Server.Identity;

[TestClass]
public class UrlValidatorTests {
    [TestMethod]
    public void ReturnUrl_LocalUrl_Accepted( ) {
        bool result = UrlValidator.IsLocalUrl( "/agents" );

        Assert.IsTrue( result );
    }

    [TestMethod]
    public void ReturnUrl_AbsoluteUrl_Rejected( ) {
        bool result = UrlValidator.IsLocalUrl( "https://evil.example.com" );

        Assert.IsFalse( result );
    }

    [TestMethod]
    public void ReturnUrl_ProtocolRelativeUrl_Rejected( ) {
        bool result = UrlValidator.IsLocalUrl( "//evil.example.com" );

        Assert.IsFalse( result );
    }

    [TestMethod]
    public void ReturnUrl_NullOrEmpty_Rejected( ) {
        Assert.IsFalse( UrlValidator.IsLocalUrl( null ) );
        Assert.IsFalse( UrlValidator.IsLocalUrl( string.Empty ) );
    }
}
