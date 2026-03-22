using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Enqueues durable notifications for agents. Producers call this service
/// within their own transaction scope; consumers are heartbeat responses.
/// </summary>
public sealed partial class AgentNotificationService(
    ILogger<AgentNotificationService> logger
) {

    /// <summary>Default TTL per notification channel.</summary>
    private static readonly Dictionary<string, TimeSpan> s_defaultTtls = new( StringComparer.Ordinal ) {
        ["schedule_invalidation"] = TimeSpan.FromHours( 1 ),
        ["key_rotation"] = TimeSpan.FromHours( 24 ),
        ["config_update"] = TimeSpan.FromHours( 1 ),
        ["workflow_disabled"] = TimeSpan.FromHours( 1 ),
        ["module_approval"] = TimeSpan.FromHours( 1 ),
    };

    /// <summary>
    /// Enqueues a notification for the specified agent on the specified channel.
    /// Deduplicates per (ConnectionId, Channel): if a notification already exists
    /// for this agent and channel that has not been drained, the write is skipped.
    /// </summary>
    /// <param name="dbContext">The current DbContext (caller's transaction scope).</param>
    /// <param name="connectionId">Target agent's connection ID.</param>
    /// <param name="channel">Notification channel name.</param>
    /// <param name="payload">Optional payload (max 2000 chars).</param>
    /// <param name="ttl">Time-to-live for this notification. Defaults to channel-specific TTL.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnqueueAsync(
        WerkrDbContext dbContext,
        Guid connectionId,
        string channel,
        string? payload = null,
        TimeSpan? ttl = null,
        CancellationToken ct = default ) {

        bool exists = await dbContext.PendingAgentNotifications
            .AnyAsync( n => n.ConnectionId == connectionId && n.Channel == channel, ct );

        if (exists) {
            LogDeduplicatedNotification( connectionId, channel );
            return;
        }

        TimeSpan effectiveTtl = ttl ?? (s_defaultTtls.TryGetValue( channel, out TimeSpan defaultTtl )
            ? defaultTtl
            : TimeSpan.FromHours( 1 ));

        DateTime now = DateTime.UtcNow;
        _ = dbContext.PendingAgentNotifications.Add( new PendingAgentNotification {
            ConnectionId = connectionId,
            Channel = channel,
            Payload = payload,
            CreatedUtc = now,
            ExpiresUtc = now.Add( effectiveTtl ),
        } );

        LogEnqueuedNotification( connectionId, channel );
    }

    /// <summary>
    /// Enqueues a notification for ALL connected (non-revoked, server-side) agents.
    /// Deduplicates per (ConnectionId, Channel).
    /// </summary>
    /// <param name="dbContext">The current DbContext (caller's transaction scope).</param>
    /// <param name="channel">Notification channel name.</param>
    /// <param name="payload">Optional payload (max 2000 chars).</param>
    /// <param name="ttl">Time-to-live for this notification. Defaults to channel-specific TTL.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnqueueForAllAsync(
        WerkrDbContext dbContext,
        string channel,
        string? payload = null,
        TimeSpan? ttl = null,
        CancellationToken ct = default ) {

        List<Guid> agentIds = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status != ConnectionStatus.Revoked )
            .Select( c => c.Id )
            .ToListAsync( ct );

        foreach (Guid agentId in agentIds) {
            await EnqueueAsync( dbContext, agentId, channel, payload, ttl, ct );
        }

        LogBroadcastNotification( channel, agentIds.Count );
    }

    [LoggerMessage( Level = LogLevel.Debug, Message = "Notification deduplicated for agent {ConnectionId} on channel {Channel}" )]
    private partial void LogDeduplicatedNotification( Guid connectionId, string channel );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Enqueued notification for agent {ConnectionId} on channel {Channel}" )]
    private partial void LogEnqueuedNotification( Guid connectionId, string channel );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Broadcast notification on channel {Channel} to {Count} agents" )]
    private partial void LogBroadcastNotification( string channel, int count );
}
