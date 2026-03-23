using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Health;

/// <summary>
/// Background service that detects stale agents based on <see cref="RegisteredConnection.LastSeen"/>
/// and transitions them to <see cref="ConnectionStatus.Disconnected"/>. Also cleans up expired
/// <see cref="PendingAgentNotification"/> rows.
/// Replaces the former <c>AgentHealthCheckService</c> which probed agents via outbound gRPC.
/// </summary>
/// <param name="scopeFactory">Service scope factory for per-sweep database contexts.</param>
/// <param name="logger">Logger for diagnostics.</param>
/// <param name="checkInterval">How often to sweep (default: 60 seconds).</param>
/// <param name="offlineThreshold">How long since LastSeen before marking offline (default: 180 seconds).</param>
public sealed partial class AgentStalenessService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentStalenessService> logger,
    TimeSpan? checkInterval = null,
    TimeSpan? offlineThreshold = null
) : BackgroundService {

    private readonly TimeSpan _checkInterval = checkInterval ?? TimeSpan.FromSeconds( 60 );
    private readonly TimeSpan _offlineThreshold = offlineThreshold ?? TimeSpan.FromSeconds( 180 );

    /// <summary>
    /// Invoked for each agent that transitions to offline. Parameters: connectionId, connectionName, cancellationToken.
    /// </summary>
    public Func<Guid, string, CancellationToken, Task>? OnAgentOffline { get; set; }

    /// <summary>
    /// Invoked for each agent that transitions to online. Parameters: connectionId, connectionName, cancellationToken.
    /// </summary>
    public Func<Guid, string, CancellationToken, Task>? OnAgentOnline { get; set; }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogServiceStarted( logger, _checkInterval, _offlineThreshold );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await SweepAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogSweepError( logger, ex );
            }

            await Task.Delay( _checkInterval, stoppingToken );
        }
    }

    private async Task SweepAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        DateTime cutoff = DateTime.UtcNow - _offlineThreshold;

        // Staleness sweep: find agents that are Connected but haven't been seen recently
        List<RegisteredConnection> staleAgents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer
                      && c.Status == ConnectionStatus.Connected
                      && c.LastSeen < cutoff )
            .ToListAsync( ct );

        foreach (RegisteredConnection agent in staleAgents) {
            agent.Status = ConnectionStatus.Disconnected;
            LogAgentMarkedOffline( logger, agent.Id, agent.ConnectionName, agent.LastSeen );

            if (OnAgentOffline is not null) {
                await OnAgentOffline( agent.Id, agent.ConnectionName, ct );
            }
        }

        // Notification expiry cleanup
        DateTime now = DateTime.UtcNow;
        int expiredCount = await dbContext.PendingAgentNotifications
            .Where( n => n.ExpiresUtc < now )
            .ExecuteDeleteAsync( ct );

        if (expiredCount > 0) {
            LogExpiredNotificationsDeleted( logger, expiredCount );
        }

        if (staleAgents.Count > 0) {
            _ = await dbContext.SaveChangesAsync( ct );
        }
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "AgentStalenessService started. CheckInterval={CheckInterval}, OfflineThreshold={OfflineThreshold}" )]
    private static partial void LogServiceStarted(
        ILogger logger, TimeSpan checkInterval, TimeSpan offlineThreshold );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Agent {AgentId} ({Name}) marked offline. LastSeen={LastSeen}" )]
    private static partial void LogAgentMarkedOffline(
        ILogger logger, Guid agentId, string name, DateTime? lastSeen );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Deleted {Count} expired pending notifications" )]
    private static partial void LogExpiredNotificationsDeleted( ILogger logger, int count );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Error in AgentStalenessService sweep" )]
    private static partial void LogSweepError( ILogger logger, Exception ex );
}
