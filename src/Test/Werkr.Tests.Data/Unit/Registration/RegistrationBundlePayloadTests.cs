using Werkr.Core.Cryptography;
using Werkr.Core.Registration.Models;

namespace Werkr.Tests.Data.Unit.Registration;

/// <summary>
/// Contains unit tests for the <see cref="RegistrationBundlePayload"/> class defined in Werkr.Core. Validates
/// round-trip encryption/decryption, wrong-password rejection, corrupted-data handling, and empty-string argument
/// validation.
/// </summary>
[TestClass]
public class RegistrationBundlePayloadTests {
    /// <summary>
    /// Verifies that encrypting and decrypting a <see cref="RegistrationBundlePayload"/> preserves all fields.
    /// </summary>
    [TestMethod]
    public void ToFromEncryptedString_RoundTrip_PreservesAllFields( ) {
        string password = "TestPassword123!";
        byte[] bundleId = EncryptionProvider.GenerateRandomBytes( 16 );
        byte[] serverPubKeyBytes = new byte[64];
        Random.Shared.NextBytes( serverPubKeyBytes );

        RegistrationBundlePayload original = new(
            bundleId,
            "TestConnection",
            "https://server:5000",
            serverPubKeyBytes
        );

        string encrypted = original.ToEncryptedString( password );
        RegistrationBundlePayload restored = RegistrationBundlePayload.FromEncryptedString(
            encrypted,
            password
        );

        CollectionAssert.AreEqual(
            original.BundleId,
            restored.BundleId
        );
        Assert.AreEqual(
            original.ConnectionName,
            restored.ConnectionName
        );
        Assert.AreEqual(
            original.ServerUrl,
            restored.ServerUrl
        );
        CollectionAssert.AreEqual(
            original.ServerPublicKeyBytes,
            restored.ServerPublicKeyBytes
        );
    }

    /// <summary>
    /// Verifies that decrypting with the wrong password throws a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void FromEncryptedString_WrongPassword_ThrowsWerkrCryptoException( ) {
        byte[] bundleId = EncryptionProvider.GenerateRandomBytes( 16 );
        byte[] keyBytes = new byte[64];
        RegistrationBundlePayload payload = new(
            bundleId,
            "Conn",
            "https://srv",
            keyBytes
        );
        string encrypted = payload.ToEncryptedString( "CorrectPassword" );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => RegistrationBundlePayload.FromEncryptedString(
            encrypted,
            "WrongPassword"
        ) );
    }

    /// <summary>
    /// Verifies that corrupted Base64 data throws a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void FromEncryptedString_CorruptedData_ThrowsWerkrCryptoException( ) {
        // Valid Base64 that is not a valid encrypted bundle
        string corrupted = Convert.ToBase64String( new byte[100] );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => RegistrationBundlePayload.FromEncryptedString(
            corrupted,
            "password"
        ) );
    }

    /// <summary>
    /// Verifies that an empty encrypted string throws <see cref="ArgumentException"/>.
    /// </summary>
    [TestMethod]
    public void FromEncryptedString_EmptyString_ThrowsArgumentException( ) {
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => RegistrationBundlePayload.FromEncryptedString(
            "",
            "password"
        ) );
    }
}
