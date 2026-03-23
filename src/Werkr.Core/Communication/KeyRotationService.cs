using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Background service that periodically generates pending AES-256-GCM key rotations
/// for every connected agent. Stores the pending key in the database and enqueues
/// a <c>key_rotation</c> notification so the agent fetches it on its next heartbeat.
/// The agent acknowledges activation via the <c>KeyExchange</c> gRPC service.
/// Previous keys are retained for a configurable grace period to handle in-flight messages.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-sweep database contexts.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="rotationInterval">How often to rotate keys (default: 24 hours).</param>
/// <param name="gracePeriod">How long to retain the previous key after rotation (default: 5 minutes).</param>
public partial class KeyRotationService(
    IServiceScopeFactory scopeFactory,
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
        AgentNotificationService notificationService =
            scope.ServiceProvider.GetRequiredService<AgentNotificationService>( );

        List<RegisteredConnection> agents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
            .ToListAsync( ct );

        if (agents.Count == 0) {
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Starting two-phase key rotation for {Count} agents.",
                agents.Count
            );
        }

        foreach (RegisteredConnection agent in agents) {
            ct.ThrowIfCancellationRequested( );
            _ = await StorePendingKeyAsync( agent, dbContext, notificationService, ct );
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
    /// Initiates a two-phase key rotation for a single agent. Generates a pending key,
    /// stores it in the database, and enqueues a notification for the agent.
    /// </summary>
    /// <param name="agentId">The connection ID of the agent to rotate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the pending key was stored successfully; false otherwise.</returns>
    public async Task<bool> RotateSingleAgentAsync(
        Guid agentId,
        CancellationToken ct
    ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        AgentNotificationService notificationService =
            scope.ServiceProvider.GetRequiredService<AgentNotificationService>( );

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

        return await StorePendingKeyAsync( agent, dbContext, notificationService, ct );
    }

    /// <summary>
    /// Generates a new AES-256 key, stores it as the pending key on the agent record,
    /// and enqueues a <c>key_rotation</c> notification so the agent fetches it.
    /// </summary>
    internal async Task<bool> StorePendingKeyAsync(
        RegisteredConnection agent,
        WerkrDbContext dbContext,
        AgentNotificationService notificationService,
        CancellationToken ct
    ) {
        try {
            // Generate new 256-bit AES key and key ID
            byte[] newKey = EncryptionProvider.GenerateRandomBytes( EncryptionProvider.AesGcmKeySize );
            string newKeyId = Guid.NewGuid( ).ToString( "N" );

            // Store the pending key on the agent record
            agent.PendingSharedKey = newKey;
            agent.PendingKeyId = newKeyId;
            _ = await dbContext.SaveChangesAsync( ct );

            // Enqueue notification so agent fetches the pending key on next heartbeat
            await notificationService.EnqueueAsync(
                dbContext, agent.Id, "key_rotation", ct: ct );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Pending key stored for Agent {AgentId} ({Name}). PendingKeyId={PendingKeyId}.",
                    agent.Id,
                    agent.ConnectionName,
                    newKeyId
                );
            }

            return true;
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
