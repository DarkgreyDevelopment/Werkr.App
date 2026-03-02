using System.Text;

using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;

namespace Werkr.Tests.Data.Unit.Cryptography;

[TestClass]
public class HybridEncryptionTests {
    private static RSAKeyPair s_keyPair = null!;

    [ClassInitialize]
    public static void ClassInit( TestContext context ) {
        // Generate once — RSA-4096 is required for hybrid operations (HybridDecrypt hardcodes 512-byte RSA block).
        s_keyPair = EncryptionProvider.GenerateRSAKeyPair( );
    }

    [TestMethod]
    public void HybridEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Hybrid encryption round-trip test data" );

        byte[] encrypted = EncryptionProvider.HybridEncrypt( plaintext, s_keyPair.PublicKey );
        byte[] decrypted = EncryptionProvider.HybridDecrypt( encrypted, s_keyPair.PrivateKey );

        CollectionAssert.AreEqual( plaintext, decrypted );
    }

    [TestMethod]
    public void HybridEncrypt_SmallPayload_ProducesCorrectEnvelopeSize( ) {
        byte[] plaintext = [1, 2, 3];

        byte[] encrypted = EncryptionProvider.HybridEncrypt( plaintext, s_keyPair.PublicKey );

        // Envelope: rsaEncryptedKey (512) + nonce (12) + tag (16) + ciphertext (same length as plaintext)
        int expectedSize = EncryptionProvider.RsaEncryptedBlockSize
            + EncryptionProvider.AesGcmNonceSize
            + EncryptionProvider.AesGcmTagSize
            + plaintext.Length;
        Assert.HasCount( expectedSize, encrypted );
    }

    [TestMethod]
    public void HybridDecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        RSAKeyPair wrongKeyPair = EncryptionProvider.GenerateRSAKeyPair( );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] encrypted = EncryptionProvider.HybridEncrypt( plaintext, s_keyPair.PublicKey );

        _ = Assert.ThrowsExactly<WerkrCryptoException>(
            ( ) => EncryptionProvider.HybridDecrypt( encrypted, wrongKeyPair.PrivateKey ) );
    }

    [TestMethod]
    public void HybridDecrypt_TamperedEnvelope_ThrowsWerkrCryptoException( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Tamper test" );
        byte[] encrypted = EncryptionProvider.HybridEncrypt( plaintext, s_keyPair.PublicKey );

        // Flip a byte in the ciphertext region (after RSA block + nonce + tag)
        int tamperIndex = EncryptionProvider.RsaEncryptedBlockSize
            + EncryptionProvider.AesGcmNonceSize
            + EncryptionProvider.AesGcmTagSize;
        encrypted[tamperIndex] ^= 0xFF;

        _ = Assert.ThrowsExactly<WerkrCryptoException>(
            ( ) => EncryptionProvider.HybridDecrypt( encrypted, s_keyPair.PrivateKey ) );
    }
}
