using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Health;

/// <summary>
/// Background service that periodically probes every non-revoked agent via
/// the encrypted <c>ConnectionManagement.Heartbeat</c> RPC and updates
/// <see cref="RegisteredConnection.Status"/> and <see cref="RegisteredConnection.LastSeen"/>
/// in the database. Replaces the former Grpc.Health.V1 probe.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-sweep database contexts.</param>
/// <param name="connectionManager">Singleton gRPC channel cache.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="interval">How often to sweep all agents (default: 60 seconds).</param>
public class AgentHealthCheckService(
    IServiceScopeFactory scopeFactory,
    AgentConnectionManager connectionManager,
    ILogger<AgentHealthCheckService> logger,
    TimeSpan? interval = null
) : BackgroundService {
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds( 60 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "AgentHealthCheckService started. Sweep interval: {Interval}.",
                _interval
            );
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await SweepAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(
                    ex,
                    "Error in AgentHealthCheckService sweep."
                );
            }

            await Task.Delay(
                _interval,
                stoppingToken
            );
        }
    }

    private async Task SweepAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Get all non-revoked server-side agents
        List<RegisteredConnection> agents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status != ConnectionStatus.Revoked )
            .ToListAsync( ct );

        if (agents.Count == 0) {
            return;
        }

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "AgentHealthCheckService sweeping {Count} agents.",
                agents.Count
            );
        }

        foreach (RegisteredConnection agent in agents) {
            ct.ThrowIfCancellationRequested( );
            await ProbeAgentAsync(
                agent,
                dbContext,
                ct
            );
        }
    }

    private async Task ProbeAgentAsync(
        RegisteredConnection agent,
        WerkrDbContext dbContext,
        CancellationToken ct
    ) {
        try {
            (
                GrpcChannel channel,
                RegisteredConnection resolved
            ) =
                await connectionManager.GetChannelAsync(
                    agent.Id,
                    ct
                );

            string keyId = resolved.ActiveKeyId ?? resolved.Id.ToString( );

            // Build and encrypt heartbeat request
            HeartbeatRequest heartbeat = new( ) {
                AgentVersion = string.Empty, // Server doesn't know the agent version; agent fills response
                UptimeSeconds = 0,
                ActiveScheduleCount = 0,
                StatusMessage = "health-probe",
            };
            EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                heartbeat, resolved.SharedKey, keyId );

            ConnectionManagement.ConnectionManagementClient client = new( channel );
            EncryptedEnvelope responseEnvelope = await client.HeartbeatAsync(
                requestEnvelope,
                AgentConnectionManager.CreateCallOptions(
                    resolved,
                    timeout: TimeSpan.FromSeconds( 5 ),
                    cancellationToken: ct
                )
                );

            // Decrypt to confirm the agent can handle our shared key
            HeartbeatResponse response = PayloadEncryptor.DecryptFromEnvelope<HeartbeatResponse>(
                responseEnvelope, resolved.SharedKey );

            // Agent is reachable and encryption is valid — mark Connected and update LastSeen
            if (agent.Status != ConnectionStatus.Connected) {
                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation(
                        "Agent {AgentId} ({Name}) transitioned from {OldStatus} to Connected.",
                        agent.Id,
                        agent.ConnectionName,
                        agent.Status
                    );
                }
                agent.Status = ConnectionStatus.Connected;
            }
            agent.LastSeen = DateTime.UtcNow;

            // Grace period cleanup: if the heartbeat succeeded with the current key,
            // the agent has confirmed the key rotation. Clear the previous key.
            if (agent.PreviousSharedKey is not null) {
                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation(
                        "Clearing PreviousSharedKey for Agent {AgentId} ({Name}). " +
                        "Heartbeat confirmed current key is active (PreviousKeyId={PreviousKeyId}).",
                        agent.Id,
                        agent.ConnectionName,
                        agent.PreviousKeyId
                    );
                }
                agent.PreviousSharedKey = null;
                agent.PreviousKeyId = null;
            }

            _ = await dbContext.SaveChangesAsync( ct );
        } catch (OperationCanceledException) {
            throw;
        } catch (RpcException ex) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "Agent {AgentId} ({Name}) unreachable: {Status}.",
                    agent.Id,
                    agent.ConnectionName,
                    ex.StatusCode
                );
            }

            // Only transition to Disconnected from Connected/Error
            if (agent.Status is ConnectionStatus.Connected or ConnectionStatus.Error) {
                agent.Status = ConnectionStatus.Disconnected;
                _ = await dbContext.SaveChangesAsync( ct );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex,
                "Unexpected error probing Agent {AgentId} ({Name}).",
                agent.Id,
                agent.ConnectionName
            );

            if (agent.Status == ConnectionStatus.Connected) {
                agent.Status = ConnectionStatus.Error;
                _ = await dbContext.SaveChangesAsync( ct );
            }
        }
    }
}
