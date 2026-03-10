using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Core.Registration.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Registration;

/// <summary>
/// Orchestrates the full server-side registration flow: bundle generation and completion.
/// </summary>
/// <remarks>
/// Creates a new <see cref="RegistrationService"/>.
/// </remarks>
/// <param name="dbContext">The database context for persisting registration data.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="serverUrl">The Server's gRPC endpoint URL embedded in bundles.</param>
public class RegistrationService(
    WerkrDbContext dbContext,
    ILogger<RegistrationService> logger,
    string serverUrl
) {

    /// <summary>
    /// Generates an encrypted registration bundle, persists it to the database,
    /// and returns the encrypted string for the admin to copy.
    /// </summary>
    /// <param name="connectionName">Admin-assigned label for this Agent connection.</param>
    /// <param name="password">Password to encrypt the bundle.</param>
    /// <param name="expiration">Optional custom expiration timespan.</param>
    /// <param name="tags">Optional tags to assign to the agent upon registration completion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encrypted bundle string (Base64-encoded).</returns>
    public async Task<string> GenerateBundleAsync(
        string connectionName,
        string password,
        TimeSpan? expiration,
        string[]? tags = null,
        CancellationToken ct = default
    ) {

        (
            string encryptedBundle,
            RegistrationBundle entity
        ) = RegistrationBundleGenerator.CreateBundle(
            connectionName, serverUrl, password, expiration: expiration );

        // Carry tags through to completion
        entity.Tags = tags ?? [];

        _ = dbContext.RegistrationBundles.Add( entity );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Registration bundle created for '{ConnectionName}', expires at {ExpiresAt}.",
                connectionName,
                entity.ExpiresAt
            );
        }

        return encryptedBundle;
    }

    /// <summary>
    /// Called when the Agent's gRPC RegisterAgent request arrives.
    /// Validates the bundle, decrypts the Agent's public key, generates API key and shared key,
    /// creates a <see cref="RegisteredConnection"/>, and returns the encrypted credentials.
    /// </summary>
    /// <param name="bundleId">The 16-byte correlation token from the Agent's request.</param>
    /// <param name="encryptedAgentPublicKey">The Agent's RSA public key, hybrid-encrypted with Server's public
    /// key.</param>
    /// <param name="agentUrl">The Agent's gRPC endpoint URL.</param>
    /// <param name="agentName">Human-readable Agent name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="AgentRegistrationResult"/> indicating success or failure.</returns>
    public async Task<(AgentRegistrationResult Result, byte[]? EncryptedResponseData)> CompleteRegistrationAsync(
        byte[] bundleId,
        byte[] encryptedAgentPublicKey,
        string agentUrl,
        string agentName,
        CancellationToken ct
    ) {

        // Look up bundle by BundleId
        RegistrationBundle? bundle = await dbContext.RegistrationBundles
            .FirstOrDefaultAsync(
                b => b.BundleId == bundleId,
                ct
            );

        if (bundle is null) {
            logger.LogWarning( "Registration attempt with unknown BundleId." );
            return (new AgentRegistrationResult(
                false,
                null,
                null,
                "Unknown registration bundle."
            ), null);
        }

        if (bundle.Status != RegistrationStatus.Pending) {
            logger.LogWarning( "Registration attempt on non-pending bundle {BundleId}, status: {Status}.",
                Convert.ToHexString( bundleId ),
                bundle.Status
            );
            return (new AgentRegistrationResult(
                false,
                null,
                null,
                $"Bundle is not pending (status: {bundle.Status})."
            ), null);
        }

        if (bundle.ExpiresAt <= DateTime.UtcNow) {
            bundle.Status = RegistrationStatus.Expired;
            _ = await dbContext.SaveChangesAsync( ct );
            logger.LogWarning( "Expired bundle used for registration attempt." );
            return (new AgentRegistrationResult(
                false,
                null,
                null,
                "Registration bundle has expired."
            ), null);
        }

        try {
            // Hybrid-decrypt Agent's public key
            byte[] agentPublicKeyBytes = EncryptionProvider.HybridDecrypt(
                encryptedAgentPublicKey,
                bundle.ServerPrivateKey
            );
            RSAParameters agentPublicKey = EncryptionProvider.DeserializePublicKey( agentPublicKeyBytes );

            // Generate bidirectional API keys: 128-char hex strings (64 random bytes each)
            string agentToServerKey = Convert.ToHexString( EncryptionProvider.GenerateRandomBytes( 64 ) );
            string serverToAgentKey = Convert.ToHexString( EncryptionProvider.GenerateRandomBytes( 64 ) );

            // Generate pre-shared symmetric key: 32-byte AES-256 key
            byte[] sharedKey = EncryptionProvider.GenerateRandomBytes( 32 );

            // Create RegisteredConnection (Server side)
            // Server stores: OutboundApiKey = raw Server→Agent key, InboundApiKeyHash = hash of Agent→Server key
            RegisteredConnection connection = new( ) {
                ConnectionName = bundle.ConnectionName,
                RemoteUrl = agentUrl,
                LocalPublicKey = bundle.ServerPublicKey,
                LocalPrivateKey = bundle.ServerPrivateKey,
                RemotePublicKey = agentPublicKey,
                OutboundApiKey = serverToAgentKey,
                InboundApiKeyHash = EncryptionProvider.HashSHA512String( agentToServerKey ),
                SharedKey = sharedKey,
                IsServer = true,
                Status = ConnectionStatus.Connected,
                Tags = bundle.Tags,
            };

            _ = dbContext.RegisteredConnections.Add( connection );
            bundle.Status = RegistrationStatus.Completed;
            _ = await dbContext.SaveChangesAsync( ct );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Registration completed for '{ConnectionName}' with Agent '{AgentName}' at {AgentUrl}.",
                    bundle.ConnectionName,
                    agentName,
                    agentUrl
                );
            }

            // Build encrypted response payload with bidirectional keys and shared connection ID
            RegistrationResponsePayload responsePayload = new(
                agentToServerKey,
                serverToAgentKey,
                sharedKey,
                connection.Id
            );
            byte[] responseJson = JsonSerializer.SerializeToUtf8Bytes( responsePayload );
            byte[] encryptedResponseData = EncryptionProvider.HybridEncrypt(
                responseJson,
                agentPublicKey
            );

            AgentRegistrationResult result = new( true, agentToServerKey, sharedKey,
                $"Registration complete. Connection '{bundle.ConnectionName}' established."
            );

            return (result, encryptedResponseData);
        } catch (WerkrCryptoException ex) {
            logger.LogError(
                ex,
                "Cryptographic error during registration completion."
            );
            return (new AgentRegistrationResult(
                false,
                null,
                null,
                "Registration failed: cryptographic error — " + ex.Message
            ), null);
        }
    }
}
