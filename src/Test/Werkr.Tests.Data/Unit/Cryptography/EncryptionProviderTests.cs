using System.Security.Cryptography;
using System.Text;

using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;

namespace Werkr.Tests.Data.Unit.Cryptography;

[TestClass]
public class EncryptionProviderTests {
    // -- RSA Key Generation --

    [TestMethod]
    public void GenerateRSAKeyPair_Default4096_ProducesValidKeyPair( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( );

        Assert.AreEqual( 4096, keyPair.KeySize );
        Assert.IsNotNull( keyPair.PublicKey.Modulus );
        Assert.HasCount( 512, keyPair.PublicKey.Modulus ); // 4096 / 8
        Assert.IsNotNull( keyPair.PrivateKey.D );
    }

    [TestMethod]
    public void GenerateRSAKeyPair_Custom2048_ProducesCorrectSize( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );

        Assert.AreEqual( 2048, keyPair.KeySize );
        Assert.IsNotNull( keyPair.PublicKey.Modulus );
        Assert.HasCount( 256, keyPair.PublicKey.Modulus ); // 2048 / 8
    }

    [TestMethod]
    public void GenerateRSAKeyPair_KeySizeTooSmall_Throws( ) {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => EncryptionProvider.GenerateRSAKeyPair( 1024 ) );
    }

    [TestMethod]
    public void GenerateRSAKeyPair_KeySizeNotDivisibleBy8_Throws( ) {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            ( ) => EncryptionProvider.GenerateRSAKeyPair( 2049 ) );
    }

    // -- RSA Encrypt / Decrypt --

    [TestMethod]
    public void RSAEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Hello, Werkr!" );

        byte[] ciphertext = EncryptionProvider.RSAEncrypt( plaintext, keyPair.PublicKey );
        byte[] decrypted = EncryptionProvider.RSADecrypt( ciphertext, keyPair.PrivateKey );

        CollectionAssert.AreEqual( plaintext, decrypted );
    }

    [TestMethod]
    public void RSADecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        RSAKeyPair keyPair1 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        RSAKeyPair keyPair2 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] ciphertext = EncryptionProvider.RSAEncrypt( plaintext, keyPair1.PublicKey );

        _ = Assert.ThrowsExactly<WerkrCryptoException>(
            ( ) => EncryptionProvider.RSADecrypt( ciphertext, keyPair2.PrivateKey ) );
    }

    // -- AES-256-GCM --

    [TestMethod]
    public void AesGcmEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] key = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] plaintext = Encoding.UTF8.GetBytes( "AES-GCM test data" );

        byte[] ciphertext = EncryptionProvider.AesGcmEncrypt( plaintext, key, out byte[] nonce, out byte[] tag );
        byte[] decrypted = EncryptionProvider.AesGcmDecrypt( ciphertext, key, nonce, tag );

        CollectionAssert.AreEqual( plaintext, decrypted );
    }

    [TestMethod]
    public void AesGcmDecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        byte[] key1 = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] key2 = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );

        byte[] ciphertext = EncryptionProvider.AesGcmEncrypt( plaintext, key1, out byte[] nonce, out byte[] tag );

        _ = Assert.ThrowsExactly<WerkrCryptoException>(
            ( ) => EncryptionProvider.AesGcmDecrypt( ciphertext, key2, nonce, tag ) );
    }

    // -- Password-based AES-GCM --

    [TestMethod]
    public void AesGcmPasswordEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Password-encrypted data" );
        string password = "StrongPassword123!";

        byte[] encrypted = EncryptionProvider.AesGcmPasswordEncrypt( plaintext, password );
        byte[] decrypted = EncryptionProvider.AesGcmPasswordDecrypt( encrypted, password );

        CollectionAssert.AreEqual( plaintext, decrypted );
    }

    [TestMethod]
    public void AesGcmPasswordDecrypt_WrongPassword_ThrowsWerkrCryptoException( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] encrypted = EncryptionProvider.AesGcmPasswordEncrypt( plaintext, "CorrectPassword" );

        _ = Assert.ThrowsExactly<WerkrCryptoException>(
            ( ) => EncryptionProvider.AesGcmPasswordDecrypt( encrypted, "WrongPassword" ) );
    }

    // -- Sign / Verify --

    [TestMethod]
    public void SignVerify_ValidSignature_ReturnsTrue( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );

        byte[] signature = EncryptionProvider.Sign( data, keyPair.PrivateKey );
        bool isValid = EncryptionProvider.Verify( data, signature, keyPair.PublicKey );

        Assert.IsTrue( isValid );
    }

    [TestMethod]
    public void Verify_WrongKey_ReturnsFalse( ) {
        RSAKeyPair keyPair1 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        RSAKeyPair keyPair2 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );
        byte[] signature = EncryptionProvider.Sign( data, keyPair1.PrivateKey );

        bool isValid = EncryptionProvider.Verify( data, signature, keyPair2.PublicKey );

        Assert.IsFalse( isValid );
    }

    [TestMethod]
    public void Verify_TamperedData_ReturnsFalse( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );
        byte[] signature = EncryptionProvider.Sign( data, keyPair.PrivateKey );

        byte[] tampered = (byte[]) data.Clone( );
        tampered[0] ^= 0xFF;

        bool isValid = EncryptionProvider.Verify( tampered, signature, keyPair.PublicKey );

        Assert.IsFalse( isValid );
    }

    // -- Serialize / Deserialize Public Key --

    [TestMethod]
    public void SerializeDeserializePublicKey_RoundTrip_PreservesKey( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );

        byte[] serialized = EncryptionProvider.SerializePublicKey( keyPair.PublicKey );
        RSAParameters deserialized = EncryptionProvider.DeserializePublicKey( serialized );

        CollectionAssert.AreEqual( keyPair.PublicKey.Modulus, deserialized.Modulus );
        CollectionAssert.AreEqual( keyPair.PublicKey.Exponent, deserialized.Exponent );
    }

    // -- Hashing --

    [TestMethod]
    public void HashSHA512String_DeterministicOutput( ) {
        string input = "test input";

        string hash1 = EncryptionProvider.HashSHA512String( input );
        string hash2 = EncryptionProvider.HashSHA512String( input );

        Assert.AreEqual( hash1, hash2 );
        Assert.HasCount( 128, hash1 ); // SHA-512 = 64 bytes = 128 hex chars
    }
}
