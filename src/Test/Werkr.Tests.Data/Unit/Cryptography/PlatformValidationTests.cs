using Werkr.Core.Cryptography;

namespace Werkr.Tests.Data.Unit.Cryptography;

[TestClass]
public class PlatformValidationTests {
    [TestMethod]
    public void ValidatePlatformCryptoSupport_OnSupportedPlatform_DoesNotThrow( ) {
        // Should not throw — SHA-512 and RSA OAEP SHA-512 are supported on all platforms.
        EncryptionProvider.ValidatePlatformCryptoSupport( );
    }
}
