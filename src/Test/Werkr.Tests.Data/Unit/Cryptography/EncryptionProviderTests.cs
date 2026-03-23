using System.Security.Cryptography;
using System.Text;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;

namespace Werkr.Tests.Data.Unit.Cryptography;

/// <summary>
/// Contains unit tests for the <see cref="EncryptionProvider"/> class defined in Werkr.Core. Validates RSA key
/// generation, RSA encrypt/decrypt, AES-GCM encrypt/decrypt, password-based encryption, digital signatures, key
/// serialization, and SHA-512 hashing.
/// </summary>
[TestClass]
public class EncryptionProviderTests {
    // -- RSA Key Generation --

    /// <summary>
    /// Verifies that the default 4096-bit RSA key pair has the correct key size and valid modulus and private exponent.
    /// </summary>
    [TestMethod]
    public void GenerateRSAKeyPair_Default4096_ProducesValidKeyPair( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( );

        Assert.AreEqual(
            4096,
            keyPair.KeySize
        );
        Assert.IsNotNull( keyPair.PublicKey.Modulus );
        Assert.HasCount(
            512,
            keyPair.PublicKey.Modulus
        ); // 4096 / 8
        Assert.IsNotNull( keyPair.PrivateKey.D );
    }

    /// <summary>
    /// Verifies that a custom 2048-bit RSA key pair has the expected key size and 256-byte modulus.
    /// </summary>
    [TestMethod]
    public void GenerateRSAKeyPair_Custom2048_ProducesCorrectSize( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );

        Assert.AreEqual(
            2048,
            keyPair.KeySize
        );
        Assert.IsNotNull( keyPair.PublicKey.Modulus );
        Assert.HasCount(
            256,
            keyPair.PublicKey.Modulus
        ); // 2048 / 8
    }

    /// <summary>
    /// Verifies that requesting a key size smaller than the minimum (2048) throws <see
    /// cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [TestMethod]
    public void GenerateRSAKeyPair_KeySizeTooSmall_Throws( ) {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>( ( ) => EncryptionProvider.GenerateRSAKeyPair( 1024 ) );
    }

    /// <summary>
    /// Verifies that requesting a key size not divisible by 8 throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    [TestMethod]
    public void GenerateRSAKeyPair_KeySizeNotDivisibleBy8_Throws( ) {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>( ( ) => EncryptionProvider.GenerateRSAKeyPair( 2049 ) );
    }

    // -- RSA Encrypt / Decrypt --

    /// <summary>
    /// Verifies that RSA encrypting and decrypting with valid key pairs returns the original plaintext.
    /// </summary>
    [TestMethod]
    public void RSAEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Hello, Werkr!" );

        byte[] ciphertext = EncryptionProvider.RSAEncrypt(
            plaintext,
            keyPair.PublicKey
        );
        byte[] decrypted = EncryptionProvider.RSADecrypt(
            ciphertext,
            keyPair.PrivateKey
        );

        CollectionAssert.AreEqual(
            plaintext,
            decrypted
        );
    }

    /// <summary>
    /// Verifies that decrypting RSA ciphertext with the wrong private key throws a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void RSADecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        RSAKeyPair keyPair1 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        RSAKeyPair keyPair2 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] ciphertext = EncryptionProvider.RSAEncrypt(
            plaintext,
            keyPair1.PublicKey
        );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => EncryptionProvider.RSADecrypt(
            ciphertext,
            keyPair2.PrivateKey
        ) );
    }

    // -- AES-256-GCM --

    /// <summary>
    /// Verifies that AES-GCM encrypting and decrypting with the same key, nonce, and tag returns the original data.
    /// </summary>
    [TestMethod]
    public void AesGcmEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] key = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] plaintext = Encoding.UTF8.GetBytes( "AES-GCM test data" );

        byte[] ciphertext = EncryptionProvider.AesGcmEncrypt(
            plaintext,
            key,
            out byte[] nonce,
            out byte[] tag
        );
        byte[] decrypted = EncryptionProvider.AesGcmDecrypt(
            ciphertext,
            key,
            nonce,
            tag
        );

        CollectionAssert.AreEqual(
            plaintext,
            decrypted
        );
    }

    /// <summary>
    /// Verifies that AES-GCM decryption with the wrong key throws a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void AesGcmDecrypt_WrongKey_ThrowsWerkrCryptoException( ) {
        byte[] key1 = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] key2 = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );

        byte[] ciphertext = EncryptionProvider.AesGcmEncrypt(
            plaintext,
            key1,
            out byte[] nonce,
            out byte[] tag
        );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => EncryptionProvider.AesGcmDecrypt(
            ciphertext,
            key2,
            nonce,
            tag
        ) );
    }

    // -- Password-based AES-GCM --

    /// <summary>
    /// Verifies that password-based AES-GCM encrypting and decrypting preserves the original data.
    /// </summary>
    [TestMethod]
    public void AesGcmPasswordEncryptDecrypt_RoundTrip_ReturnsOriginalData( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Password-encrypted data" );
        string password = "StrongPassword123!";

        byte[] encrypted = EncryptionProvider.AesGcmPasswordEncrypt(
            plaintext,
            password
        );
        byte[] decrypted = EncryptionProvider.AesGcmPasswordDecrypt(
            encrypted,
            password
        );

        CollectionAssert.AreEqual(
            plaintext,
            decrypted
        );
    }

    /// <summary>
    /// Verifies that password-based AES-GCM decryption with the wrong password throws a <see
    /// cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void AesGcmPasswordDecrypt_WrongPassword_ThrowsWerkrCryptoException( ) {
        byte[] plaintext = Encoding.UTF8.GetBytes( "Secret" );
        byte[] encrypted = EncryptionProvider.AesGcmPasswordEncrypt(
            plaintext,
            "CorrectPassword"
        );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => EncryptionProvider.AesGcmPasswordDecrypt(
            encrypted,
            "WrongPassword"
        ) );
    }

    // -- Sign / Verify --

    /// <summary>
    /// Verifies that signing data and verifying with the matching public key returns <see langword="true"/>.
    /// </summary>
    [TestMethod]
    public void SignVerify_ValidSignature_ReturnsTrue( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );

        byte[] signature = EncryptionProvider.Sign(
            data,
            keyPair.PrivateKey
        );
        bool isValid = EncryptionProvider.Verify(
            data,
            signature,
            keyPair.PublicKey
        );

        Assert.IsTrue( isValid );
    }

    /// <summary>
    /// Verifies that verifying a signature with a different public key returns <see langword="false"/>.
    /// </summary>
    [TestMethod]
    public void Verify_WrongKey_ReturnsFalse( ) {
        RSAKeyPair keyPair1 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        RSAKeyPair keyPair2 = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );
        byte[] signature = EncryptionProvider.Sign(
            data,
            keyPair1.PrivateKey
        );

        bool isValid = EncryptionProvider.Verify(
            data,
            signature,
            keyPair2.PublicKey
        );

        Assert.IsFalse( isValid );
    }

    /// <summary>
    /// Verifies that verifying a signature against tampered data returns <see langword="false"/>.
    /// </summary>
    [TestMethod]
    public void Verify_TamperedData_ReturnsFalse( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );
        byte[] data = Encoding.UTF8.GetBytes( "Sign this data" );
        byte[] signature = EncryptionProvider.Sign(
            data,
            keyPair.PrivateKey
        );

        byte[] tampered = (byte[]) data.Clone( );
        tampered[0] ^= 0xFF;

        bool isValid = EncryptionProvider.Verify(
            tampered,
            signature,
            keyPair.PublicKey
        );

        Assert.IsFalse( isValid );
    }

    // -- Serialize / Deserialize Public Key --

    /// <summary>
    /// Verifies that serializing and deserializing an RSA public key preserves the modulus and exponent.
    /// </summary>
    [TestMethod]
    public void SerializeDeserializePublicKey_RoundTrip_PreservesKey( ) {
        RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( 2048 );

        byte[] serialized = EncryptionProvider.SerializePublicKey( keyPair.PublicKey );
        RSAParameters deserialized = EncryptionProvider.DeserializePublicKey( serialized );

        CollectionAssert.AreEqual(
            keyPair.PublicKey.Modulus,
            deserialized.Modulus
        );
        CollectionAssert.AreEqual(
            keyPair.PublicKey.Exponent,
            deserialized.Exponent
        );
    }

    // -- Hashing --

    /// <summary>
    /// Verifies that <see cref="HashSHA512String"/> produces the same 128-character hex output for identical inputs.
    /// </summary>
    [TestMethod]
    public void HashSHA512String_DeterministicOutput( ) {
        string input = "test input";

        string hash1 = EncryptionProvider.HashSHA512String( input );
        string hash2 = EncryptionProvider.HashSHA512String( input );

        Assert.AreEqual(
            hash1,
            hash2
        );
        Assert.HasCount(
            128,
            hash1
        ); // SHA-512 = 64 bytes = 128 hex chars
    }
}
