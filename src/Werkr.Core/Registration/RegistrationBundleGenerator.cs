using System.Security.Cryptography;
using Werkr.Core.Cryptography;
using Werkr.Core.Registration.Models;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Registration;

/// <summary>
/// Creates encrypted registration bundles for admin-carried handoff to Agents.
/// </summary>
public static class RegistrationBundleGenerator {
    /// <summary>
    /// Creates an encrypted registration bundle and its corresponding database entity.
    /// The admin copies the encrypted string and carries it to the Agent's localhost registration page.
    /// </summary>
    /// <param name="connectionName">Admin-assigned label for this Agent connection.</param>
    /// <param name="serverUrl">The Server's gRPC endpoint URL that the Agent will call back.</param>
    /// <param name="password">Password used to AES-GCM encrypt the bundle.</param>
    /// <param name="keySize">RSA key size in bits (default 4096).</param>
    /// <param name="expiration">How long the bundle stays valid (default 24 hours).</param>
    /// <returns>Tuple of the encrypted bundle string and the entity to persist.</returns>
    public static (
        string EncryptedBundle,
        RegistrationBundle Entity
    ) CreateBundle(
        string connectionName,
        string serverUrl,
        string password,
        int keySize = 4096,
        TimeSpan? expiration = null
    ) {

        // Generate RSA key pair for this registration
        Cryptography.KeyInfo.RSAKeyPair keyPair = EncryptionProvider.GenerateRSAKeyPair( keySize );

        // Generate random 16-byte correlation token
        byte[] bundleId = EncryptionProvider.GenerateRandomBytes( 16 );

        // Serialize server's public key
        byte[] serverPublicKeyBytes = EncryptionProvider.SerializePublicKey( keyPair.PublicKey );

        // Build payload
        RegistrationBundlePayload payload = new(
            BundleId: bundleId,
            ConnectionName: connectionName,
            ServerUrl: serverUrl,
            ServerPublicKeyBytes: serverPublicKeyBytes
        );

        // Encrypt payload with password
        string encryptedBundle = payload.ToEncryptedString( password );

        // Derive 256-bit AES key from the password using the same SHA-512 truncation
        // that AesGcmPasswordEncrypt uses. The Agent derives the identical key from the
        // same password, so both sides share a symmetric key for the registration RPC.
        byte[] registrationKey = DeriveRegistrationKey( password );

        // Create entity
        DateTime expiresAt = expiration switch {
            null => DateTime.UtcNow + TimeSpan.FromHours( 24 ),
            TimeSpan timeSpan when timeSpan <= TimeSpan.Zero => DateTime.MaxValue,
            TimeSpan timeSpan => DateTime.UtcNow + timeSpan
        };

        RegistrationBundle entity = new( ) {
            ConnectionName = connectionName,
            ServerPublicKey = keyPair.PublicKey,
            ServerPrivateKey = keyPair.PrivateKey,
            BundleId = bundleId,
            Status = Common.Models.RegistrationStatus.Pending,
            ExpiresAt = expiresAt,
            KeySize = keySize,
            RegistrationKey = registrationKey,
        };

        return (encryptedBundle, entity);
    }

    /// <summary>
    /// Derives a 32-byte AES-256 key from a password using SHA-512 truncation.
    /// Uses the same derivation as <see cref="EncryptionProvider.AesGcmPasswordEncrypt"/>:
    /// SHA-512 hash of the UTF-8 password bytes, truncated to 32 bytes.
    /// Both Server and Agent call this with the same password to produce an identical key.
    /// </summary>
    /// <param name="password">The bundle password.</param>
    /// <returns>A 32-byte AES-256 key.</returns>
    public static byte[] DeriveRegistrationKey( string password ) {
        byte[] fullHash = SHA512.HashData( System.Text.Encoding.UTF8.GetBytes( password ) );
        return fullHash[..EncryptionProvider.AesGcmKeySize];
    }
}
