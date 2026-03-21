using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Configuration;

/// <summary>
/// Pushes configuration change notifications to connected agents via gRPC.
/// Global changes notify all agents; agent-scoped changes notify only that agent.
/// </summary>
public sealed partial class ConfigurationChangeNotifier(
    WerkrDbContext dbContext,
    AgentConnectionManager connectionManager,
    ILogger<ConfigurationChangeNotifier> logger
) {

    /// <summary>
    /// Notifies affected agents that configuration has changed.
    /// </summary>
    /// <param name="newVersion">The new sync version number.</param>
    /// <param name="scopeId">If non-null, only notify this specific agent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task NotifyAsync( long newVersion, string? scopeId, CancellationToken ct ) {
        List<RegisteredConnection> agents;

        if (!string.IsNullOrEmpty( scopeId ) && Guid.TryParse( scopeId, out Guid agentGuid )) {
            // Agent-scoped change — notify only that agent
            RegisteredConnection? agent = await dbContext.RegisteredConnections
                .AsNoTracking( )
                .FirstOrDefaultAsync( c => c.Id == agentGuid && c.IsServer && c.Status == ConnectionStatus.Connected, ct );
            agents = agent is not null ? [agent] : [];
        } else {
            // Global change — notify all connected agents
            agents = await dbContext.RegisteredConnections
                .AsNoTracking( )
                .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
                .ToListAsync( ct );
        }

        NotifyConfigurationChangedRequest grpcRequest = new( ) {
            NewVersion = newVersion,
        };

        int notified = 0;
        foreach (RegisteredConnection agent in agents) {
            try {
                (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection conn) =
                    await connectionManager.GetChannelAsync( agent.Id, ct );

                string keyId = conn.ActiveKeyId ?? conn.Id.ToString( );
                EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                    grpcRequest, conn.SharedKey, keyId );

                Grpc.Core.CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
                    conn, timeout: TimeSpan.FromSeconds( 10 ), cancellationToken: ct );

                ConnectionManagement.ConnectionManagementClient client = new( channel );
                EncryptedEnvelope responseEnvelope =
                    await client.NotifyConfigurationChangedAsync( envelope, callOptions );

                NotifyConfigurationChangedResponse response =
                    PayloadEncryptor.DecryptFromEnvelope<NotifyConfigurationChangedResponse>(
                        responseEnvelope, conn.SharedKey );

                if (response.Acknowledged) {
                    notified++;
                }
            } catch (Exception ex) {
                LogNotifyFailed( logger, agent.Id, agent.ConnectionName, ex );
            }
        }

        LogNotifyComplete( logger, notified, agents.Count );
    }

    [LoggerMessage( Level = LogLevel.Warning, Message = "Failed to notify agent {AgentId} ({Name}) of config change." )]
    private static partial void LogNotifyFailed( ILogger logger, Guid agentId, string name, Exception ex );

    [LoggerMessage( Level = LogLevel.Information, Message = "Configuration change notification: {Notified}/{Total} agents acknowledged." )]
    private static partial void LogNotifyComplete( ILogger logger, int notified, int total );
}
