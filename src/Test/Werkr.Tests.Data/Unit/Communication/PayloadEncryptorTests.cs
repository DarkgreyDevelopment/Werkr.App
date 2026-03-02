using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;

namespace Werkr.Tests.Data.Unit.Communication;

[TestClass]
public class PayloadEncryptorTests {
    private byte[] _sharedKey = null!;
    private const string TestKeyId = "test-key-1";

    [TestInitialize]
    public void TestInit( ) {
        _sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
    }

    [TestMethod]
    public void EncryptDecryptEnvelope_RoundTrip( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "Hello, encrypted world!" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( envelope, _sharedKey );

        Assert.AreEqual( original.StatusMessage, decrypted.StatusMessage );
    }

    [TestMethod]
    public void EncryptDecryptEnvelope_EmptyMessage_RoundTrip( ) {
        HeartbeatRequest original = new( );

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( envelope, _sharedKey );

        Assert.AreEqual( original.StatusMessage, decrypted.StatusMessage );
    }

    [TestMethod]
    public void EncryptDecryptEnvelope_LargePayload_RoundTrip( ) {
        HeartbeatRequest original = new( ) { StatusMessage = new string( 'A', 100_000 ) };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( envelope, _sharedKey );

        Assert.AreEqual( original.StatusMessage, decrypted.StatusMessage );
    }

    [TestMethod]
    public void EncryptToEnvelope_SetsKeyId( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "test payload" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        Assert.AreEqual( TestKeyId, envelope.KeyId );
        Assert.IsFalse( envelope.Ciphertext.IsEmpty );
        Assert.IsFalse( envelope.Iv.IsEmpty );
        Assert.IsFalse( envelope.AuthTag.IsEmpty );
    }

    [TestMethod]
    public void EncryptToEnvelope_DifferentIvEachCall( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "same plaintext" };

        EncryptedEnvelope envelope1 = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );
        EncryptedEnvelope envelope2 = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        Assert.AreNotEqual( envelope1.Iv, envelope2.Iv );
    }

    [TestMethod]
    public void DecryptFromEnvelope_WrongKey_Throws( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "secret data" };
        byte[] wrongKey = EncryptionProvider.GenerateRandomBytes( 32 );

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( envelope, wrongKey ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_TamperedCiphertext_Throws( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        byte[] cipherBytes = envelope.Ciphertext.ToByteArray( );
        cipherBytes[^1] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = Google.Protobuf.ByteString.CopyFrom( cipherBytes ),
            Iv = envelope.Iv,
            AuthTag = envelope.AuthTag,
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( tampered, _sharedKey ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_TamperedIv_Throws( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        byte[] ivBytes = envelope.Iv.ToByteArray( );
        ivBytes[0] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = envelope.Ciphertext,
            Iv = Google.Protobuf.ByteString.CopyFrom( ivBytes ),
            AuthTag = envelope.AuthTag,
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( tampered, _sharedKey ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_TamperedAuthTag_Throws( ) {
        HeartbeatRequest original = new( ) { StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, _sharedKey, TestKeyId );

        byte[] tagBytes = envelope.AuthTag.ToByteArray( );
        tagBytes[0] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = envelope.Ciphertext,
            Iv = envelope.Iv,
            AuthTag = Google.Protobuf.ByteString.CopyFrom( tagBytes ),
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) =>
            PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>( tampered, _sharedKey ) );
    }

    [TestMethod]
    public void DecryptFromEnvelope_KeyRotation_DecryptsWithCurrentKey( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] previousKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string previousKeyId = "key-1";

        HeartbeatRequest original = new( ) { StatusMessage = "rotated" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, currentKey, currentKeyId );

        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope, currentKey, currentKeyId, previousKey, previousKeyId );

        Assert.AreEqual( original.StatusMessage, decrypted.StatusMessage );
    }

    [TestMethod]
    public void DecryptFromEnvelope_KeyRotation_DecryptsWithPreviousKey( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] previousKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string previousKeyId = "key-1";

        HeartbeatRequest original = new( ) { StatusMessage = "in-flight" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( original, previousKey, previousKeyId );

        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope, currentKey, currentKeyId, previousKey, previousKeyId );

        Assert.AreEqual( original.StatusMessage, decrypted.StatusMessage );
    }
}
