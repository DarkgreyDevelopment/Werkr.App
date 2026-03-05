using System.Text;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;

namespace Werkr.Tests.Data.Unit.Cryptography;

/// <summary>
/// Contains unit tests for the hybrid RSA+AES-GCM encryption methods in <see cref="EncryptionProvider"/>. Validates
/// round-trip correctness, envelope size, wrong-key rejection, and tamper detection.
/// </summary>
[TestClass]
public class HybridEncryptionTests {
    /// <summary>
    /// The RSA key pair shared across all tests in this class.
    /// </summary>
    private static RSAKeyPair s_keyPair = null!;

    /// <summary>
    /// Generates the RSA key pair once for all tests in this class.
    /// </summary>
    [ClassInitialize]
    public static void ClassInit( TestContext context ) {
        // Generate once — RSA-4096 is required for hybrid operations (HybridDecrypt hardcodes 512-byte RSA block).
        s_keyPair = EncryptionProvider.GenerateRSAKeyPair( );
    }

    /// <summary>
    /// Verifies that hybrid encrypting and decrypting with the correct key pair returns the original plaintext.
    /// </summary>
    [TestMethod]
    public void HybridEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Hybrid encryption round-trip test data" );

        byte[] encrypted = EncryptionProvider.HybridEncrypt(
            plaintext,
            s_keyPair.PublicKey
        );
        byte[] decrypted = EncryptionProvider.HybridDecrypt(
            encrypted,
            s_keyPair.PrivateKey
        );

        CollectionAssert.AreEqual(
            plaintext,
            decrypted
        );
    }

    /// <summary>
    /// Verifies that a small payload produces an encrypted envelope of the expected size (RSA block + nonce + tag +
    /// plaintext length).
    /// </summary>
    [TestMethod]
    public void HybridEncrypt_SmallPayload_ProducesCorrectEnvelopeSize( ) {
        byte[] plaintext = [1, 2, 3];

        byte[] encrypted = EncryptionProvider.HybridEncrypt(
            plaintext,
            s_keyPair.PublicKey
        );

        // Envelope: rsaEncryptedKey (512) + nonce (12) + tag (16) + ciphertext (same length as plaintext)
        int expectedSize = EncryptionProvider.RsaEncryptedBlockSize
            + EncryptionProvider.AesGcmNonceSize
            + EncryptionProvider.AesGcmTagSize
            + plaintext.Length;
        Assert.HasCount(
            expectedSize,
            encrypted
        );
    }

    /// <summary>
    /// Verifies that decrypting a hybrid envelope with the wrong RSA private key throws a <see
    /// cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void HybridDecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        RSAKeyPair wrongKeyPair = EncryptionProvider.GenerateRSAKeyPair( );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] encrypted = EncryptionProvider.HybridEncrypt(
            plaintext,
            s_keyPair.PublicKey
        );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => EncryptionProvider.HybridDecrypt(
            encrypted,
            wrongKeyPair.PrivateKey
        ));
    }

    /// <summary>
    /// Verifies that tampering with the ciphertext portion of a hybrid envelope throws a <see
    /// cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void HybridDecrypt_TamperedEnvelope_ThrowsWerkrCryptoException( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Tamper test" );
        byte[] encrypted = EncryptionProvider.HybridEncrypt(
            plaintext,
            s_keyPair.PublicKey
        );

        // Flip a byte in the ciphertext region (after RSA block + nonce + tag)
        int tamperIndex = EncryptionProvider.RsaEncryptedBlockSize
            + EncryptionProvider.AesGcmNonceSize
            + EncryptionProvider.AesGcmTagSize;
        encrypted[tamperIndex] ^= 0xFF;

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => EncryptionProvider.HybridDecrypt(
            encrypted,
            s_keyPair.PrivateKey
        ));
    }
}
