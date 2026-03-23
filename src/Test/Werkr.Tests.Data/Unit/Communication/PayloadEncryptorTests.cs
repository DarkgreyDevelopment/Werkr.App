using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Contains unit tests for the <see cref="PayloadEncryptor"/> class defined in Werkr.Core. Validates encrypt/decrypt
/// round-trips, key ID handling, IV uniqueness, wrong-key rejection, tampered-data detection (ciphertext, IV, auth
/// tag), and key rotation decryption.
/// </summary>
[TestClass]
public class PayloadEncryptorTests {
    /// <summary>
    /// The AES-256 shared key generated before each test.
    /// </summary>
    private byte[] _sharedKey = null!;
    /// <summary>
    /// The key identifier used across test envelopes.
    /// </summary>
    private const string TestKeyId = "test-key-1";

    /// <summary>
    /// Generates a fresh 32-byte shared key for each test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );
    }

    /// <summary>
    /// Verifies that encrypting and decrypting an <see cref="AgentHeartbeatRequest"/> preserves the message content.
    /// </summary>
    [TestMethod]
    public void EncryptDecryptEnvelope_RoundTrip( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "Hello, encrypted world!" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );
        AgentHeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            _sharedKey
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that an empty <see cref="AgentHeartbeatRequest"/> round-trips correctly through encryption.
    /// </summary>
    [TestMethod]
    public void EncryptDecryptEnvelope_EmptyMessage_RoundTrip( ) {
        AgentHeartbeatRequest original = new( );

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );
        AgentHeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            _sharedKey
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that a large payload (100,000 characters) round-trips correctly through encryption.
    /// </summary>
    [TestMethod]
    public void EncryptDecryptEnvelope_LargePayload_RoundTrip( ) {
        AgentHeartbeatRequest original = new( ) {
            StatusMessage = new string(
            'A',
            100_000
        )
        };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );
        AgentHeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            _sharedKey
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that the encrypted envelope carries the expected key ID and non-empty ciphertext, IV, and auth tag.
    /// </summary>
    [TestMethod]
    public void EncryptToEnvelope_SetsKeyId( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "test payload" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        Assert.AreEqual(
            TestKeyId,
            envelope.KeyId
        );
        Assert.IsFalse( envelope.Ciphertext.IsEmpty );
        Assert.IsFalse( envelope.Iv.IsEmpty );
        Assert.IsFalse( envelope.AuthTag.IsEmpty );
    }

    /// <summary>
    /// Verifies that two envelopes for the same plaintext use different initialization vectors.
    /// </summary>
    [TestMethod]
    public void EncryptToEnvelope_DifferentIvEachCall( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "same plaintext" };

        EncryptedEnvelope envelope1 = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );
        EncryptedEnvelope envelope2 = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        Assert.AreNotEqual(
            envelope1.Iv,
            envelope2.Iv
        );
    }

    /// <summary>
    /// Verifies that decryption with the wrong shared key throws a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_WrongKey_Throws( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "secret data" };
        byte[] wrongKey = EncryptionProvider.GenerateRandomBytes( 32 );

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            wrongKey
        ) );
    }

    /// <summary>
    /// Verifies that tampered ciphertext is detected and results in a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_TamperedCiphertext_Throws( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        byte[] cipherBytes = envelope.Ciphertext.ToByteArray( );
        cipherBytes[^1] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = Google.Protobuf.ByteString.CopyFrom( cipherBytes ),
            Iv = envelope.Iv,
            AuthTag = envelope.AuthTag,
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            tampered,
            _sharedKey
        ) );
    }

    /// <summary>
    /// Verifies that a tampered initialization vector is detected and results in a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_TamperedIv_Throws( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        byte[] ivBytes = envelope.Iv.ToByteArray( );
        ivBytes[0] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = envelope.Ciphertext,
            Iv = Google.Protobuf.ByteString.CopyFrom( ivBytes ),
            AuthTag = envelope.AuthTag,
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            tampered,
            _sharedKey
        ) );
    }

    /// <summary>
    /// Verifies that a tampered authentication tag is detected and results in a <see cref="WerkrCryptoException"/>.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_TamperedAuthTag_Throws( ) {
        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "secret data" };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            _sharedKey,
            TestKeyId
        );

        byte[] tagBytes = envelope.AuthTag.ToByteArray( );
        tagBytes[0] ^= 0xFF;

        EncryptedEnvelope tampered = new( ) {
            Ciphertext = envelope.Ciphertext,
            Iv = envelope.Iv,
            AuthTag = Google.Protobuf.ByteString.CopyFrom( tagBytes ),
            KeyId = envelope.KeyId,
        };

        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            tampered,
            _sharedKey
        ) );
    }

    /// <summary>
    /// Verifies that the key-rotation overload decrypts correctly when the envelope uses the current key.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_KeyRotation_DecryptsWithCurrentKey( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] previousKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string previousKeyId = "key-1";

        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "rotated" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            currentKey,
            currentKeyId
        );

        AgentHeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            currentKey,
            currentKeyId,
            previousKey,
            previousKeyId
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that the key-rotation overload decrypts correctly when the envelope uses the previous key.
    /// </summary>
    [TestMethod]
    public void DecryptFromEnvelope_KeyRotation_DecryptsWithPreviousKey( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] previousKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string previousKeyId = "key-1";

        AgentHeartbeatRequest original = new( ) { ConnectionId = "test", AgentVersion = "1.0", StatusMessage = "in-flight" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            previousKey,
            previousKeyId
        );

        AgentHeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatRequest>(
            envelope,
            currentKey,
            currentKeyId,
            previousKey,
            previousKeyId
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }
}
