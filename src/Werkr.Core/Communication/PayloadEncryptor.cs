using Google.Protobuf;
using Werkr.Common.Protos;
using Werkr.Core.Cryptography;

namespace Werkr.Core.Communication;

/// <summary>
/// Encrypts and decrypts gRPC payloads using AES-256-GCM with the connection's
/// pre-shared <c>SharedKey</c>. All gRPC messages (except Registration.proto)
/// are wrapped in an <see cref="EncryptedEnvelope"/>.
/// </summary>
public static class PayloadEncryptor {
    /// <summary>
    /// Encrypts a protobuf message into an <see cref="EncryptedEnvelope"/>.
    /// </summary>
    /// <typeparam name="T">The protobuf message type.</typeparam>
    /// <param name="message">The plaintext protobuf message to encrypt.</param>
    /// <param name="sharedKey">The 32-byte AES-256 symmetric key.</param>
    /// <param name="keyId">Identifier for the key used (supports key rotation).</param>
    /// <returns>An <see cref="EncryptedEnvelope"/> containing the encrypted payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sharedKey"/> is null.</exception>
    public static EncryptedEnvelope EncryptToEnvelope<T>(
        T message,
        byte[] sharedKey,
        string keyId
    )
        where T : IMessage<T> {
        ArgumentNullException.ThrowIfNull(
            sharedKey,
            nameof( sharedKey )
        );

        byte[] plaintext = message.ToByteArray( );
        byte[] ciphertext = EncryptionProvider.AesGcmEncrypt(
            plaintext,
            sharedKey,
            out byte[] nonce,
            out byte[] tag
        );

        return new EncryptedEnvelope {
            Ciphertext = ByteString.CopyFrom( ciphertext ),
            Iv = ByteString.CopyFrom( nonce ),
            AuthTag = ByteString.CopyFrom( tag ),
            KeyId = keyId,
        };
    }

    /// <summary>
    /// Decrypts an <see cref="EncryptedEnvelope"/> back to a protobuf message.
    /// </summary>
    /// <typeparam name="T">The protobuf message type.</typeparam>
    /// <param name="envelope">The encrypted envelope to decrypt.</param>
    /// <param name="sharedKey">The 32-byte AES-256 symmetric key.</param>
    /// <returns>The decrypted protobuf message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sharedKey"/> is null.</exception>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails (wrong key or tampered data).</exception>
    public static T DecryptFromEnvelope<T>(
        EncryptedEnvelope envelope,
        byte[] sharedKey
    )
        where T : IMessage<T>, new() {
        ArgumentNullException.ThrowIfNull(
            sharedKey,
            nameof( sharedKey )
        );

        byte[] plaintext = EncryptionProvider.AesGcmDecrypt(
            envelope.Ciphertext.ToByteArray( ),
            sharedKey,
            envelope.Iv.ToByteArray( ),
            envelope.AuthTag.ToByteArray( )
        );

        MessageParser<T> parser = new( ( ) => new T( ) );
        return parser.ParseFrom( plaintext );
    }

    /// <summary>
    /// Decrypts an <see cref="EncryptedEnvelope"/> with key rotation support.
    /// Tries the current key first. If the envelope's <c>KeyId</c> matches
    /// <paramref name="previousKeyId"/> and a previous key is available, falls back to that.
    /// </summary>
    /// <typeparam name="T">The protobuf message type.</typeparam>
    /// <param name="envelope">The encrypted envelope to decrypt.</param>
    /// <param name="currentKey">The current 32-byte AES-256 symmetric key.</param>
    /// <param name="currentKeyId">Key ID for the current key.</param>
    /// <param name="previousKey">The previous key (may be null if no rotation in progress).</param>
    /// <param name="previousKeyId">Key ID for the previous key (may be null).</param>
    /// <returns>The decrypted protobuf message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="currentKey"/> is null.</exception>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails with all available keys.</exception>
    public static T DecryptFromEnvelope<T>(
        EncryptedEnvelope envelope,
        byte[] currentKey,
        string currentKeyId,
        byte[]? previousKey,
        string? previousKeyId
    )
        where T : IMessage<T>, new() {
        ArgumentNullException.ThrowIfNull(
            currentKey,
            nameof( currentKey )
        );

        // If the key ID matches the current key, or no key ID is set, use current key
        if (string.IsNullOrEmpty( envelope.KeyId ) || envelope.KeyId == currentKeyId) {
            return DecryptFromEnvelope<T>(
                envelope,
                currentKey
            );
        }

        // If the key ID matches the previous key and a previous key exists, use it
        if (previousKey is not null && previousKeyId is not null && envelope.KeyId == previousKeyId) {
            return DecryptFromEnvelope<T>(
                envelope,
                previousKey
            );
        }

        // Key ID doesn't match any known key — try current key as a last resort
        // (handles the case where key IDs haven't been synchronized yet)
        return DecryptFromEnvelope<T>(
            envelope,
            currentKey
        );
    }
}
