using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Configuration;

/// <summary>
/// Enqueues configuration change notifications for connected agents via the notification outbox.
/// Global changes notify all agents; agent-scoped changes notify only that agent.
/// </summary>
public sealed partial class ConfigurationChangeNotifier(
    WerkrDbContext dbContext,
    AgentNotificationService notificationService,
    ILogger<ConfigurationChangeNotifier> logger
) {

    /// <summary>
    /// Enqueues configuration change notifications for affected agents.
    /// </summary>
    /// <param name="newVersion">The new sync version number.</param>
    /// <param name="scopeId">If non-null, only notify this specific agent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task NotifyAsync( long newVersion, string? scopeId, CancellationToken ct ) {
        string payload = newVersion.ToString( );

        if (!string.IsNullOrEmpty( scopeId ) && Guid.TryParse( scopeId, out Guid agentGuid )) {
            // Agent-scoped change — notify only that agent
            RegisteredConnection? agent = await dbContext.RegisteredConnections
                .AsNoTracking( )
                .FirstOrDefaultAsync( c => c.Id == agentGuid && c.IsServer && c.Status == ConnectionStatus.Connected, ct );

            if (agent is not null) {
                await notificationService.EnqueueAsync(
                    dbContext, agentGuid, "config_update", payload, ct: ct );
                LogNotifyEnqueued( logger, 1, 1 );
            } else {
                LogNotifyEnqueued( logger, 0, 0 );
            }
        } else {
            // Global change — notify all connected agents
            await notificationService.EnqueueForAllAsync(
                dbContext, "config_update", payload, ct: ct );
            LogNotifyEnqueuedGlobal( logger );
        }
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Configuration change notification enqueued: {Enqueued}/{Total} agents" )]
    private static partial void LogNotifyEnqueued( ILogger logger, int enqueued, int total );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Configuration change notification enqueued for all connected agents" )]
    private static partial void LogNotifyEnqueuedGlobal( ILogger logger );
}
