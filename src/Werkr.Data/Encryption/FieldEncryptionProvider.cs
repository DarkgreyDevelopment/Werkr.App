using System.Security.Cryptography;

namespace Werkr.Data.Encryption;

/// <summary>
/// Provides transparent field-level encryption using AES-256-GCM for sensitive
/// <see cref="Werkr.Data.Entities.Registration.RegisteredConnection"/> columns
/// (<c>OutboundApiKey</c>, <c>LocalPrivateKey</c>, <c>SharedKey</c>).
/// <para>
/// The symmetric passphrase is sourced from the OS secret store via
/// <c>ISecretStore</c> (DPAPI on Windows, Keychain on macOS, libsecret on Linux).
/// A new passphrase is auto-generated on first use and stored under the key
/// <c>werkr-pgcrypto-passphrase</c>.
/// </para>
/// <para>
/// This implementation performs encryption at the application level, making it
/// database-provider-agnostic. When the Agent runs on Postgres, the encrypted
/// <c>bytea</c> payload is stored directly; when on SQLite/SQLCipher, it is a
/// secondary layer on top of the whole-DB encryption.
/// </para>
/// </summary>
public sealed class FieldEncryptionProvider {
    private readonly byte[] _key;

    /// <summary>
    /// Initialises a new <see cref="FieldEncryptionProvider"/> with a 32-byte encryption key.
    /// </summary>
    /// <param name="base64Key">Base64-encoded 32-byte AES-256 key from the OS secret store.</param>
    /// <exception cref="ArgumentException">Thrown when the decoded key is not exactly 32 bytes.</exception>
    public FieldEncryptionProvider( string base64Key ) {
        _key = Convert.FromBase64String( base64Key );
        if (_key.Length != 32) {
            throw new ArgumentException( "Encryption key must be exactly 32 bytes (256 bits).", nameof( base64Key ) );
        }
    }

    /// <summary>The OS secret store key under which the passphrase is stored.</summary>
    public const string SecretStoreKey = "werkr-pgcrypto-passphrase";

    /// <summary>
    /// Generates a new random 32-byte key encoded as Base64.
    /// </summary>
    public static string GenerateKey( ) =>
        Convert.ToBase64String( RandomNumberGenerator.GetBytes( 32 ) );

    /// <summary>
    /// Encrypts a plaintext string using AES-256-GCM and returns a Base64-encoded ciphertext
    /// (nonce ‖ ciphertext ‖ tag).
    /// </summary>
    /// <param name="plaintext">The value to encrypt.</param>
    /// <returns>Base64-encoded encrypted blob, or <c>null</c> if <paramref name="plaintext"/> is <c>null</c>.</returns>
    public string? Encrypt( string? plaintext ) {
        if (plaintext is null) {
            return null;
        }

        byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes( plaintext );
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill( nonce );

        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using AesGcm aes = new( _key, AesGcm.TagByteSizes.MaxSize );
        aes.Encrypt( nonce, plaintextBytes, ciphertext, tag );

        // Pack: [nonce (12)] [ciphertext (N)] [tag (16)]
        byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo( result, 0 );
        ciphertext.CopyTo( result, nonce.Length );
        tag.CopyTo( result, nonce.Length + ciphertext.Length );

        return Convert.ToBase64String( result );
    }

    /// <summary>
    /// Decrypts a Base64-encoded AES-256-GCM blob (nonce ‖ ciphertext ‖ tag) back to plaintext.
    /// </summary>
    /// <param name="encryptedBase64">The Base64-encoded encrypted blob.</param>
    /// <returns>The decrypted plaintext, or <c>null</c> if <paramref name="encryptedBase64"/> is <c>null</c>.</returns>
    /// <exception cref="AuthenticationTagMismatchException">If the tag is invalid (data tampered).</exception>
    public string? Decrypt( string? encryptedBase64 ) {
        if (encryptedBase64 is null) {
            return null;
        }

        byte[] blob = Convert.FromBase64String( encryptedBase64 );
        const int NonceSize = 12;
        const int TagSize = 16;

        if (blob.Length < NonceSize + TagSize) {
            throw new ArgumentException( "Encrypted blob is too short.", nameof( encryptedBase64 ) );
        }

        byte[] nonce = blob[..NonceSize];
        byte[] tag = blob[^TagSize..];
        byte[] ciphertext = blob[NonceSize..^TagSize];

        byte[] plaintext = new byte[ciphertext.Length];

        using AesGcm aes = new( _key, AesGcm.TagByteSizes.MaxSize );
        aes.Decrypt( nonce, ciphertext, tag, plaintext );

        return System.Text.Encoding.UTF8.GetString( plaintext );
    }

    /// <summary>
    /// Encrypts a byte array using AES-256-GCM and returns a Base64-encoded ciphertext.
    /// </summary>
    public string? EncryptBytes( byte[]? data ) {
        if (data is null || data.Length == 0) {
            return null;
        }

        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill( nonce );

        byte[] ciphertext = new byte[data.Length];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using AesGcm aes = new( _key, AesGcm.TagByteSizes.MaxSize );
        aes.Encrypt( nonce, data, ciphertext, tag );

        byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        nonce.CopyTo( result, 0 );
        ciphertext.CopyTo( result, nonce.Length );
        tag.CopyTo( result, nonce.Length + ciphertext.Length );

        return Convert.ToBase64String( result );
    }

    /// <summary>
    /// Decrypts a Base64-encoded AES-256-GCM blob back to a byte array.
    /// </summary>
    public byte[]? DecryptBytes( string? encryptedBase64 ) {
        if (encryptedBase64 is null) {
            return null;
        }

        byte[] blob = Convert.FromBase64String( encryptedBase64 );
        const int NonceSize = 12;
        const int TagSize = 16;

        if (blob.Length < NonceSize + TagSize) {
            throw new ArgumentException( "Encrypted blob is too short.", nameof( encryptedBase64 ) );
        }

        byte[] nonce = blob[..NonceSize];
        byte[] tag = blob[^TagSize..];
        byte[] ciphertext = blob[NonceSize..^TagSize];

        byte[] plaintext = new byte[ciphertext.Length];

        using AesGcm aes = new( _key, AesGcm.TagByteSizes.MaxSize );
        aes.Decrypt( nonce, ciphertext, tag, plaintext );

        return plaintext;
    }
}
