using System.Security.Cryptography;
using System.Text.Json;
using Grpc.Net.Client;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Core.Registration.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Registration;

/// <summary>
/// Processes a registration bundle on the Agent side: decrypts the bundle,
/// generates the Agent's RSA keypair, calls the Server's gRPC RegisterAgent
/// endpoint, and persists the resulting connection to the local database.
/// </summary>
/// <remarks>
/// Creates a new <see cref="AgentRegistrationHandler"/>.
/// </remarks>
/// <param name="logger">Logger for diagnostics.</param>
public class AgentRegistrationHandler( ILogger<AgentRegistrationHandler> logger ) {

    /// <summary>
    /// Processes a registration bundle pasted by the admin, generates the Agent's
    /// RSA keypair, calls the Server's RegisterAgent gRPC endpoint, and persists
    /// the resulting connection to the local database.
    /// </summary>
    /// <param name="encryptedBundle">The Base64-encoded encrypted bundle string from the Server admin.</param>
    /// <param name="password">The password used to encrypt the bundle.</param>
    /// <param name="agentUrl">The Agent's gRPC endpoint URL.</param>
    /// <param name="dbContext">The Agent's local database context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="AgentRegistrationResult"/> indicating success or failure.</returns>
    public async Task<AgentRegistrationResult> ProcessBundleAsync(
        string encryptedBundle,
        string password,
        string agentUrl,
        WerkrDbContext dbContext,
        CancellationToken ct
    ) {

        // 1. Decrypt the bundle payload
        RegistrationBundlePayload payload;
        try {
            payload = RegistrationBundlePayload.FromEncryptedString( encryptedBundle, password );
        } catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException) {
            if (logger.IsEnabled( LogLevel.Warning )) {
                logger.LogWarning( ex, "Failed to decrypt registration bundle. Check password and bundle integrity." );
            }

            return new AgentRegistrationResult( false, null, null,
                "Failed to decrypt registration bundle. Verify the password and that the bundle was copied correctly." );
        }

        // 2. Generate Agent's own RSA keypair
        RSAKeyPair agentKeyPair = EncryptionProvider.GenerateRSAKeyPair( 4096 );

        // 3. Hybrid-encrypt Agent's public key with Server's public key
        byte[] agentPublicKeyBytes = EncryptionProvider.SerializePublicKey( agentKeyPair.PublicKey );
        RSAParameters serverPublicKey = EncryptionProvider.DeserializePublicKey( payload.ServerPublicKeyBytes );
        byte[] encryptedAgentPublicKey = EncryptionProvider.HybridEncrypt( agentPublicKeyBytes, serverPublicKey );

        // 4. Create gRPC channel and call RegisterAgent
        using GrpcChannel channel = GrpcChannel.ForAddress( payload.ServerUrl );
        AgentRegistration.AgentRegistrationClient client = new( channel );

        RegisterAgentRequest request = new( ) {
            BundleId = Google.Protobuf.ByteString.CopyFrom( payload.BundleId ),
            EncryptedAgentPublicKey = Google.Protobuf.ByteString.CopyFrom( encryptedAgentPublicKey ),
            AgentUrl = agentUrl,
            AgentName = Environment.MachineName
        };

        RegisterAgentResponse response;
        try {
            response = await client.RegisterAgentAsync( request, cancellationToken: ct );
        } catch (Exception ex) {
            if (logger.IsEnabled( LogLevel.Error )) {
                logger.LogError( ex, "gRPC call to Server's RegisterAgent endpoint failed." );
            }

            return new AgentRegistrationResult( false, null, null,
                $"Failed to contact the Server at {payload.ServerUrl}. Verify the Server is running and reachable." );
        }

        if (!response.Success) {
            return new AgentRegistrationResult( false, null, null, response.Message );
        }

        // 5. Hybrid-decrypt the registration response data
        byte[] decryptedResponseBytes;
        try {
            decryptedResponseBytes = EncryptionProvider.HybridDecrypt(
                response.EncryptedRegistrationData.ToByteArray( ),
                agentKeyPair.PrivateKey );
        } catch (WerkrCryptoException ex) {
            if (logger.IsEnabled( LogLevel.Error )) {
                logger.LogError( ex, "Failed to decrypt registration response data." );
            }

            return new AgentRegistrationResult( false, null, null,
                "Registration succeeded on the Server but the response could not be decrypted." );
        }

        RegistrationResponsePayload? responsePayload = JsonSerializer.Deserialize<RegistrationResponsePayload>(
            decryptedResponseBytes );
        if (responsePayload is null) {
            return new AgentRegistrationResult( false, null, null,
                "Registration succeeded but the response payload was invalid." );
        }

        // 6. Persist RegisteredConnection locally
        // Use the shared ConnectionId from the Server so both sides reference the same ID
        // Agent stores: OutboundApiKey = raw Agent→Server key, InboundApiKeyHash = hash of Server→Agent key
        RegisteredConnection connection = new( ) {
            Id = responsePayload.ConnectionId,
            ConnectionName = payload.ConnectionName,
            RemoteUrl = payload.ServerUrl,
            LocalPublicKey = agentKeyPair.PublicKey,
            LocalPrivateKey = agentKeyPair.PrivateKey,
            RemotePublicKey = serverPublicKey,
            OutboundApiKey = responsePayload.AgentToServerApiKey,
            InboundApiKeyHash = EncryptionProvider.HashSHA512String( responsePayload.ServerToAgentApiKey ),
            SharedKey = responsePayload.SharedKey,
            IsServer = false,
            Status = ConnectionStatus.Connected,
        };

        try {
            _ = dbContext.RegisteredConnections.Add( connection );
            _ = await dbContext.SaveChangesAsync( ct );
        } catch (Exception ex) {
            if (logger.IsEnabled( LogLevel.Error )) {
                logger.LogError( ex,
                    "Registration succeeded on Server but failed to save locally for connection '{ConnectionName}'.",
                    payload.ConnectionName );
            }

            return new AgentRegistrationResult( false, null, null,
                "Registration succeeded on the Server but failed to save locally. " +
                "The admin should revoke the orphaned connection on the Server (via /agents page) and generate a new bundle." );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Successfully registered with Server at {ServerUrl} as '{ConnectionName}'.",
                payload.ServerUrl, payload.ConnectionName );
        }

        return new AgentRegistrationResult( true, responsePayload.AgentToServerApiKey, responsePayload.SharedKey,
            $"Registration complete. Connection '{payload.ConnectionName}' established with Server at {payload.ServerUrl}." );
    }
}
