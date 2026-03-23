using Werkr.Core.Cryptography;

namespace Werkr.Tests.Data.Unit.Cryptography;

/// <summary>
/// Contains unit tests that verify the current platform supports the required cryptographic primitives.
/// </summary>
[TestClass]
public class PlatformValidationTests {
    /// <summary>
    /// Verifies that <see cref="EncryptionProvider.ValidatePlatformCryptoSupport"/> does not throw on a supported
    /// platform.
    /// </summary>
    [TestMethod]
    public void ValidatePlatformCryptoSupport_OnSupportedPlatform_DoesNotThrow( ) {
        // Should not throw — SHA-512 and RSA OAEP SHA-512 are supported on all platforms.
        EncryptionProvider.ValidatePlatformCryptoSupport( );
    }
}
