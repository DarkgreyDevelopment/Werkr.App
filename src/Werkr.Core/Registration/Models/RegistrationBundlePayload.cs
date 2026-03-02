using System.Text.Json;

using Werkr.Core.Cryptography;

namespace Werkr.Core.Registration.Models;

/// <summary>
/// The data inside the encrypted registration bundle that the admin carries from Server to Agent.
/// Contains the Server's public key, connection metadata, and the correlation token.
/// </summary>
/// <param name="BundleId">16-byte random correlation token identifying the pending registration.</param>
/// <param name="ConnectionName">Admin-assigned label for this Agent connection.</param>
/// <param name="ServerUrl">The Server's gRPC endpoint URL that the Agent will call back.</param>
/// <param name="ServerPublicKeyBytes">Serialized RSA public key bytes (via <see cref="EncryptionProvider.SerializePublicKey"/>).</param>
public sealed record RegistrationBundlePayload(
    byte[] BundleId,
    string ConnectionName,
    string ServerUrl,
    byte[] ServerPublicKeyBytes ) {

    /// <summary>
    /// Serializes this payload to JSON, encrypts it with a password via AES-GCM,
    /// and returns the result as a Base64-encoded string.
    /// </summary>
    /// <param name="password">The password to encrypt the bundle with.</param>
    /// <returns>A Base64-encoded encrypted string.</returns>
    public string ToEncryptedString( string password ) {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes( this );
        byte[] encrypted = EncryptionProvider.AesGcmPasswordEncrypt( json, password );
        return Convert.ToBase64String( encrypted );
    }

    /// <summary>
    /// Decrypts and deserializes a <see cref="RegistrationBundlePayload"/> from a Base64-encoded encrypted string.
    /// </summary>
    /// <param name="encrypted">The Base64-encoded encrypted string produced by <see cref="ToEncryptedString"/>.</param>
    /// <param name="password">The password used during encryption.</param>
    /// <returns>The deserialized payload.</returns>
    /// <exception cref="ArgumentException">Thrown when the input is null or empty.</exception>
    /// <exception cref="WerkrCryptoException">Thrown when decryption fails (wrong password or corrupted data).</exception>
    public static RegistrationBundlePayload FromEncryptedString( string encrypted, string password ) {
        if (string.IsNullOrWhiteSpace( encrypted )) {
            throw new ArgumentException( "Encrypted bundle string cannot be null or empty.", nameof( encrypted ) );
        }

        byte[] encryptedBytes;
        try {
            encryptedBytes = Convert.FromBase64String( encrypted );
        } catch (FormatException ex) {
            throw new WerkrCryptoException( "Invalid Base64 format in encrypted bundle string.", ex );
        }

        byte[] json = EncryptionProvider.AesGcmPasswordDecrypt( encryptedBytes, password );
        RegistrationBundlePayload? payload = JsonSerializer.Deserialize<RegistrationBundlePayload>( json );
        return payload ?? throw new WerkrCryptoException( "Failed to deserialize registration bundle payload." );
    }
}
