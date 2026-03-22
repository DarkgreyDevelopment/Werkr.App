using System.Reflection;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Services;

/// <summary>
/// API-hosted gRPC service for agent heartbeats. Agents call this periodically
/// to report liveness; the API responds with acknowledgment, server version,
/// and any pending notification channels from the outbox.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-call database contexts.</param>
/// <param name="builder">Secure request/response builder.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class AgentHeartbeatGrpcService(
    IServiceScopeFactory scopeFactory,
    SecureResponseBuilder builder,
    ILogger<AgentHeartbeatGrpcService> logger
) : AgentHeartbeat.AgentHeartbeatBase {

    private static readonly string s_serverVersion =
        Assembly.GetEntryAssembly( )
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>( )
            ?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Processes an agent heartbeat: updates LastSeen and Status, drains pending
    /// notifications, and returns an acknowledgment with server version.
    /// </summary>
    public override async Task<EncryptedEnvelope> Heartbeat(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        (RegisteredConnection connection, AgentHeartbeatRequest inner) =
            SecureResponseBuilder.DecryptRequest<AgentHeartbeatRequest>( request, context );

        CancellationToken ct = context.CancellationToken;

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Load the tracked connection
        RegisteredConnection? agent = await dbContext.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == connection.Id && c.IsServer, ct );

        if (agent is null) {
            throw new RpcException( new Status( StatusCode.NotFound,
                "Agent connection not found." ) );
        }

        // Update LastSeen and transition to Connected if needed
        agent.LastSeen = DateTime.UtcNow;
        if (agent.Status != ConnectionStatus.Connected) {
            ConnectionStatus previousStatus = agent.Status;
            agent.Status = ConnectionStatus.Connected;
            LogAgentReconnected( logger, agent.Id, agent.ConnectionName, previousStatus );
        }

        // Update AgentVersion if reported and different
        if (!string.IsNullOrEmpty( inner.AgentVersion )
            && inner.AgentVersion != agent.AgentVersion) {
            agent.AgentVersion = inner.AgentVersion;
        }

        // Drain pending notifications for this agent
        List<PendingAgentNotification> pending = await dbContext.PendingAgentNotifications
            .Where( n => n.ConnectionId == connection.Id && n.ExpiresUtc > DateTime.UtcNow )
            .OrderBy( n => n.CreatedUtc )
            .ToListAsync( ct );

        // Build response
        AgentHeartbeatResponse response = new( ) {
            Acknowledged = true,
            ServerVersion = s_serverVersion,
        };

        foreach (PendingAgentNotification notification in pending) {
            response.PendingNotifications.Add( new PendingNotification {
                Channel = notification.Channel,
                Payload = notification.Payload ?? string.Empty,
            } );
        }

        // Remove drained notifications
        if (pending.Count > 0) {
            dbContext.PendingAgentNotifications.RemoveRange( pending );
            LogNotificationsDrained( logger, agent.Id, pending.Count );
        }

        // Single SaveChangesAsync for LastSeen update + notification drain
        _ = await dbContext.SaveChangesAsync( ct );

        LogHeartbeatProcessed( logger, agent.Id, agent.ConnectionName );

        return await builder.EncryptResponseAsync( response, connection, ct );
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Agent {AgentId} ({Name}) reconnected from {PreviousStatus} to Connected" )]
    private static partial void LogAgentReconnected(
        ILogger logger, Guid agentId, string name, ConnectionStatus previousStatus );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Heartbeat processed for agent {AgentId} ({Name})" )]
    private static partial void LogHeartbeatProcessed( ILogger logger, Guid agentId, string name );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Drained {Count} pending notifications for agent {AgentId}" )]
    private static partial void LogNotificationsDrained( ILogger logger, Guid agentId, int count );
}
