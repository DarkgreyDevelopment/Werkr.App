using System.Security.Cryptography;

using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Cryptography;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Background service that periodically rotates the AES-256-GCM <c>SharedKey</c>
/// for every connected agent. Generates a new key, RSA-encrypts it with the
/// Agent's public key, and sends it via the <c>RotateSharedKey</c> gRPC RPC.
/// On success, the previous key is retained for a grace period to handle in-flight messages.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-sweep database contexts.</param>
/// <param name="connectionManager">Singleton gRPC channel cache.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="rotationInterval">How often to rotate keys (default: 24 hours).</param>
public class KeyRotationService(
    IServiceScopeFactory scopeFactory,
    AgentConnectionManager connectionManager,
    ILogger<KeyRotationService> logger,
    TimeSpan? rotationInterval = null
) : BackgroundService {
    private readonly TimeSpan _rotationInterval = rotationInterval ?? TimeSpan.FromHours( 24 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "KeyRotationService started. Rotation interval: {Interval}.",
                _rotationInterval
            );
        }

        while (!stoppingToken.IsCancellationRequested) {
            // Wait first, then rotate — gives the system time to stabilize after startup
            await Task.Delay(
                _rotationInterval,
                stoppingToken
            );

            try {
                await RotateAllAgentsAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(
                    ex,
                    "Error in KeyRotationService sweep."
                );
            }
        }
    }

    private async Task RotateAllAgentsAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        List<RegisteredConnection> agents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
            .ToListAsync( ct );

        if (agents.Count == 0) {
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Starting key rotation for {Count} agents.",
                agents.Count
            );
        }

        foreach (RegisteredConnection agent in agents) {
            ct.ThrowIfCancellationRequested( );
            _ = await RotateAgentKeyAsync(
                agent,
                dbContext,
                ct
            );
        }
    }

    /// <summary>
    /// Rotates the shared key for a single agent.
    /// Called by both the background sweep and the manual rotation endpoint.
    /// </summary>
    /// <param name="agentId">The connection ID of the agent to rotate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the rotation succeeded; false otherwise.</returns>
    public async Task<bool> RotateSingleAgentAsync(
        Guid agentId,
        CancellationToken ct
    ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        RegisteredConnection? agent = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync(
                c => c.Id == agentId && c.IsServer && c.Status == ConnectionStatus.Connected,
                ct
            );

        if (agent is null) {
            logger.LogWarning(
                "Agent {AgentId} not found or not connected for key rotation.",
                agentId
            );
            return false;
        }

        return await RotateAgentKeyAsync(
            agent,
            dbContext,
            ct
        );
    }

    internal async Task<bool> RotateAgentKeyAsync(
        RegisteredConnection agent,
        WerkrDbContext dbContext,
        CancellationToken ct
    ) {
        try {
            // 1. Generate new 256-bit AES key and key ID
            byte[] newKey = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
            string newKeyId = Guid.NewGuid( ).ToString( "N" );

            // 2. RSA-encrypt the new key with the Agent's public key
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( agent.RemotePublicKey );
            byte[] rsaEncryptedNewKey = rsa.Encrypt(
                newKey,
                RSAEncryptionPadding.OaepSHA256
            );

            // 3. Send RotateSharedKey RPC via the existing encrypted channel
            (
                GrpcChannel channel,
                RegisteredConnection resolved
            ) =
                await connectionManager.GetChannelAsync(
                    agent.Id,
                    ct
                );

            string currentKeyId = resolved.ActiveKeyId ?? resolved.Id.ToString( );

            RotateSharedKeyRequest rotationRequest = new( ) {
                RsaEncryptedNewKey = ByteString.CopyFrom( rsaEncryptedNewKey ),
                NewKeyId = newKeyId,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                rotationRequest, resolved.SharedKey, currentKeyId );

            CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
                resolved,
                timeout: TimeSpan.FromSeconds( 30 ),
                cancellationToken: ct
            );

            ConnectionManagement.ConnectionManagementClient client = new( channel );
            EncryptedEnvelope responseEnvelope = await client.RotateSharedKeyAsync(
                envelope,
                callOptions
            );

            // 4. The agent responds with the NEW key, so decrypt with the new key
            RotateSharedKeyResponse response = PayloadEncryptor.DecryptFromEnvelope<RotateSharedKeyResponse>(
                responseEnvelope, newKey );

            if (!response.Success) {
                logger.LogWarning(
                    "Agent {AgentId} ({Name}) rejected key rotation. ActiveKeyId={ActiveKeyId}.",
                    agent.Id,
                    agent.ConnectionName,
                    response.ActiveKeyId
                );
                return false;
            }

            // 5. Persist: move current key to previous, install new key on API side
            agent.PreviousSharedKey = agent.SharedKey;
            agent.PreviousKeyId = agent.ActiveKeyId;
            agent.SharedKey = newKey;
            agent.ActiveKeyId = newKeyId;
            _ = await dbContext.SaveChangesAsync( ct );

            // 6. Reset the cached channel so it picks up the refreshed connection
            connectionManager.RemoveChannel( agent.Id );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Key rotation succeeded for Agent {AgentId} ({Name}). NewKeyId={NewKeyId}.",
                    agent.Id,
                    agent.ConnectionName,
                    newKeyId
                );
            }

            return true;
        } catch (RpcException ex) {
            logger.LogWarning( ex,
                "Key rotation RPC failed for Agent {AgentId} ({Name}). Status={Status}.",
                agent.Id,
                agent.ConnectionName,
                ex.StatusCode
            );
            return false;
        } catch (CryptographicException ex) {
            logger.LogError( ex,
                "Key rotation cryptographic failure for Agent {AgentId} ({Name}).",
                agent.Id,
                agent.ConnectionName
            );
            return false;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError( ex,
                "Unexpected error during key rotation for Agent {AgentId} ({Name}).",
                agent.Id,
                agent.ConnectionName
            );
            return false;
        }
    }
}
