using System.Security.Cryptography;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Common.Protos;
using Werkr.Core.Audit;
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
/// <param name="gracePeriod">How long to retain the previous key after rotation (default: 5 minutes).</param>
public partial class KeyRotationService(
    IServiceScopeFactory scopeFactory,
    AgentConnectionManager connectionManager,
    ILogger<KeyRotationService> logger,
    TimeSpan? rotationInterval = null,
    TimeSpan? gracePeriod = null
) : BackgroundService {
    private readonly TimeSpan _rotationInterval = rotationInterval ?? TimeSpan.FromHours( 24 );
    private readonly TimeSpan _gracePeriod = gracePeriod ?? TimeSpan.FromMinutes( 5 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "KeyRotationService started. Rotation interval: {Interval}, Grace period: {GracePeriod}.",
                _rotationInterval,
                _gracePeriod
            );
        }

        DateTime nextRotation = DateTime.UtcNow + _rotationInterval;
        TimeSpan graceCheckInterval = TimeSpan.FromSeconds( 60 );

        while (!stoppingToken.IsCancellationRequested) {
            await Task.Delay( graceCheckInterval, stoppingToken );

            // Grace period cleanup (runs every 60s)
            try {
                await ClearExpiredGracePeriodsAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError( ex, "Error clearing expired grace periods." );
            }

            // Key rotation (runs on original interval)
            if (DateTime.UtcNow >= nextRotation) {
                try {
                    await RotateAllAgentsAsync( stoppingToken );
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogError( ex, "Key rotation sweep failed." );
                }
                nextRotation = DateTime.UtcNow + _rotationInterval;
            }
        }
    }

    private async Task RotateAllAgentsAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        IAuditService? auditService = scope.ServiceProvider.GetService<IAuditService>( );

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
                auditService,
                ct
            );
        }
    }

    /// <summary>
    /// Clears <see cref="RegisteredConnection.PreviousSharedKey"/> for agents whose grace
    /// period has expired (i.e., <see cref="RegisteredConnection.KeyRotatedAtUtc"/> is older
    /// than <see cref="_gracePeriod"/>).
    /// </summary>
    internal async Task ClearExpiredGracePeriodsAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        DateTime cutoff = DateTime.UtcNow - _gracePeriod;

        List<RegisteredConnection> expired = await dbContext.RegisteredConnections
            .Where( c => c.IsServer
                && c.PreviousSharedKey != null
                && c.KeyRotatedAtUtc != null
                && c.KeyRotatedAtUtc < cutoff )
            .ToListAsync( ct );

        foreach (RegisteredConnection agent in expired) {
            agent.PreviousSharedKey = null;
            agent.PreviousKeyId = null;
            agent.KeyRotatedAtUtc = null;
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Grace period expired for agent {AgentId}. Previous key cleared.", agent.Id );
            }
        }

        if (expired.Count > 0) {
            _ = await dbContext.SaveChangesAsync( ct );
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
            null,
            ct
        );
    }

    internal async Task<bool> RotateAgentKeyAsync(
        RegisteredConnection agent,
        WerkrDbContext dbContext,
        IAuditService? auditService,
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
            agent.KeyRotatedAtUtc = DateTime.UtcNow;
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

            // Audit: background key rotation — best-effort, don't fail the rotation
            if (auditService is not null) {
                try {
                    await auditService.LogAsync( new AuditEntry(
                        EventTypeId: AuditEventType.AgentKeyRotated.ToEventId( ),
                        ActorId: null, ActorType: "System",
                        EntityType: "Agent", EntityId: agent.Id.ToString( ),
                        ActionPerformed: "KeyRotated",
                        Details: new { AgentName = agent.ConnectionName, Source = "BackgroundRotation" }
                    ), ct );
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    logger.LogWarning( ex,
                        "Failed to record audit event for key rotation of Agent {AgentId}.", agent.Id );
                }
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
