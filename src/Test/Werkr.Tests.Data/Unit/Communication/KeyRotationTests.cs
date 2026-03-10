using System.Security.Cryptography;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Tests key rotation flows: round-trip RSA encryption of new keys,
/// grace period handling via <see cref="PayloadEncryptor"/> key rotation overload,
/// and key ID matching logic.
/// </summary>
[TestClass]
public class KeyRotationTests {
    /// <summary>
    /// Verifies that a new shared key encrypted with an agent's RSA public key can be decrypted with the matching
    /// private key.
    /// </summary>
    [TestMethod]
    public void RotationRoundTrip_RsaEncryptNewKey_AgentDecrypts( ) {
        // Simulate: API generates new key, RSA-encrypts with Agent's public key
        RSAKeyPair agentKeys = EncryptionProvider.GenerateRSAKeyPair( );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );

        using RSA rsaEncrypt = RSA.Create( );
        rsaEncrypt.ImportParameters( agentKeys.PublicKey );
        byte[] rsaEncryptedNewKey = rsaEncrypt.Encrypt(
            newKey,
            RSAEncryptionPadding.OaepSHA256
        );

        // Simulate: Agent decrypts with its private key
        using RSA rsaDecrypt = RSA.Create( );
        rsaDecrypt.ImportParameters( agentKeys.PrivateKey );
        byte[] decryptedKey = rsaDecrypt.Decrypt(
            rsaEncryptedNewKey,
            RSAEncryptionPadding.OaepSHA256
        );

        CollectionAssert.AreEqual(
            newKey,
            decryptedKey
        );
    }

    /// <summary>
    /// Verifies that decrypting an RSA-encrypted key with the wrong private key throws a <see
    /// cref="System.Security.Cryptography.CryptographicException"/>.
    /// </summary>
    [TestMethod]
    public void RotationRoundTrip_WrongPrivateKey_Throws( ) {
        RSAKeyPair agentKeys = EncryptionProvider.GenerateRSAKeyPair( );
        RSAKeyPair wrongKeys = EncryptionProvider.GenerateRSAKeyPair( );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );

        using RSA rsaEncrypt = RSA.Create( );
        rsaEncrypt.ImportParameters( agentKeys.PublicKey );
        byte[] rsaEncryptedNewKey = rsaEncrypt.Encrypt(
            newKey,
            RSAEncryptionPadding.OaepSHA256
        );

        // Attempt to decrypt with wrong private key
        using RSA rsaDecrypt = RSA.Create( );
        rsaDecrypt.ImportParameters( wrongKeys.PrivateKey );

        // macOS throws a platform-specific CryptographicException subclass,
        // so we catch the base type instead of using ThrowsExactly.
        bool threw = false;
        try {
            _ = rsaDecrypt.Decrypt(
                rsaEncryptedNewKey,
                RSAEncryptionPadding.OaepSHA256
            );
        } catch (CryptographicException) {
            threw = true;
        }
        Assert.IsTrue(
            threw,
            "Expected CryptographicException when decrypting with wrong key."
        );
    }

    /// <summary>
    /// Verifies that the old key can still decrypt messages encrypted before rotation when both old and new keys are
    /// supplied (grace period).
    /// </summary>
    [TestMethod]
    public void GracePeriod_OldKeyStillDecryptsDuringTransition( ) {
        byte[] oldKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string oldKeyId = "key-1";
        string newKeyId = "key-2";

        // Message encrypted with old key (in-flight during rotation)
        HeartbeatRequest original = new( ) { StatusMessage = "in-flight message" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            oldKey,
            oldKeyId
        );

        // Receiver has rotated to new key but still holds old key as previous
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope,
            newKey,
            newKeyId,
            oldKey,
            oldKeyId
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that the new key can decrypt messages encrypted after rotation completes when both old and new keys are
    /// supplied.
    /// </summary>
    [TestMethod]
    public void GracePeriod_NewKeyDecryptsPostRotation( ) {
        byte[] oldKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string oldKeyId = "key-1";
        string newKeyId = "key-2";

        // Message encrypted with new key (post-rotation)
        HeartbeatRequest original = new( ) { StatusMessage = "post-rotation message" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            newKey,
            newKeyId
        );

        // Receiver has rotated and holds old key as previous
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope,
            newKey,
            newKeyId,
            oldKey,
            oldKeyId
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that decryption succeeds when no previous key is provided and the message was encrypted with the
    /// current key.
    /// </summary>
    [TestMethod]
    public void GracePeriod_NoPreviousKey_DecryptsWithCurrentOnly( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-1";

        HeartbeatRequest original = new( ) { StatusMessage = "no previous key" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            currentKey,
            currentKeyId
        );

        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope,
            currentKey,
            currentKeyId,
            null,
            null
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that an envelope with an unknown key ID falls back to the current key for decryption when no previous
    /// key is available.
    /// </summary>
    [TestMethod]
    public void GracePeriod_UnknownKeyId_FallsBackToCurrentKey( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string unknownKeyId = "key-unknown";

        // Encrypt with current key but use an unknown key ID
        HeartbeatRequest original = new( ) { StatusMessage = "unknown key id" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            currentKey,
            unknownKeyId
        );

        // Should fall back to current key as last resort
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope,
            currentKey,
            currentKeyId,
            null,
            null
        );

        Assert.AreEqual(
            original.StatusMessage,
            decrypted.StatusMessage
        );
    }

    /// <summary>
    /// Verifies that decryption throws a <see cref="WerkrCryptoException"/> when the previous key is no longer
    /// available and the envelope was encrypted with the old key.
    /// </summary>
    [TestMethod]
    public void GracePeriod_ExpiredPreviousKey_FailsAfterGracePeriod( ) {
        byte[] oldKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string newKeyId = "key-2";

        // Message encrypted with old key — but the previous key slot has been cleared
        HeartbeatRequest original = new( ) { StatusMessage = "expired" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            oldKey,
            "key-1"
        );

        // Receiver no longer has the old key (grace period expired)
        _ = Assert.ThrowsExactly<WerkrCryptoException>( ( ) => PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            envelope,
            newKey,
            newKeyId,
            null,
            null
        ) );
    }

    /// <summary>
    /// Verifies the full rotation protocol: RSA-encrypts a new shared key into a <see cref="RotateSharedKeyRequest"/>
    /// envelope, decrypts it, and recovers the key.
    /// </summary>
    [TestMethod]
    public void RotationProtocol_EnvelopeContainsRotationRequest( ) {
        // Verify the full envelope round-trip for a RotateSharedKeyRequest
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-1";

        RSAKeyPair agentKeys = EncryptionProvider.GenerateRSAKeyPair( );
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );

        using RSA rsa = RSA.Create( );
        rsa.ImportParameters( agentKeys.PublicKey );
        byte[] rsaEncryptedNewKey = rsa.Encrypt(
            newKey,
            RSAEncryptionPadding.OaepSHA256
        );

        RotateSharedKeyRequest rotationRequest = new( ) {
            RsaEncryptedNewKey = ByteString.CopyFrom( rsaEncryptedNewKey ),
            NewKeyId = "key-2",
        };

        // Encrypt with current SharedKey
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            rotationRequest,
            currentKey,
            currentKeyId
        );

        // Decrypt with current SharedKey
        RotateSharedKeyRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<RotateSharedKeyRequest>(
            envelope,
            currentKey
        );

        Assert.AreEqual(
            "key-2",
            decrypted.NewKeyId
        );

        // Agent side: decrypt the RSA payload
        using RSA agentRsa = RSA.Create( );
        agentRsa.ImportParameters( agentKeys.PrivateKey );
        byte[] recoveredKey = agentRsa.Decrypt(
            decrypted.RsaEncryptedNewKey.ToByteArray( ),
            RSAEncryptionPadding.OaepSHA256
        );

        CollectionAssert.AreEqual(
            newKey,
            recoveredKey
        );
    }

    /// <summary>
    /// Verifies that a <see cref="RotateSharedKeyResponse"/> encrypted with the new key can be decrypted and contains
    /// the expected success status and active key ID.
    /// </summary>
    [TestMethod]
    public void RotationResponse_EncryptedWithNewKey_Decrypts( ) {
        byte[] newKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string newKeyId = "key-2";

        // Agent encrypts response with the newly activated key
        RotateSharedKeyResponse response = new( ) {
            Success = true,
            ActiveKeyId = newKeyId,
        };

        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            response,
            newKey,
            newKeyId
        );

        // API decrypts with the new key it generated
        RotateSharedKeyResponse decrypted = PayloadEncryptor.DecryptFromEnvelope<RotateSharedKeyResponse>(
            envelope,
            newKey
        );

        Assert.IsTrue( decrypted.Success );
        Assert.AreEqual(
            newKeyId,
            decrypted.ActiveKeyId
        );
    }

    /// <summary>
    /// Verifies that the key ID is preserved in the envelope and that the rotation overload correctly selects the
    /// previous key for decryption based on the key ID.
    /// </summary>
    [TestMethod]
    public void KeyIdPreserved_AcrossRotationOverload( ) {
        byte[] currentKey = EncryptionProvider.GenerateRandomBytes( 32 );
        byte[] previousKey = EncryptionProvider.GenerateRandomBytes( 32 );
        string currentKeyId = "key-2";
        string previousKeyId = "key-1";

        // Encrypt with previous key, verify key ID is preserved in envelope
        HeartbeatRequest original = new( ) { StatusMessage = "check key id" };
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
            original,
            previousKey,
            previousKeyId
        );

        Assert.AreEqual(
            previousKeyId,
            envelope.KeyId
        );

        // Rotation overload selects the right key based on key ID
        HeartbeatRequest decrypted = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
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
