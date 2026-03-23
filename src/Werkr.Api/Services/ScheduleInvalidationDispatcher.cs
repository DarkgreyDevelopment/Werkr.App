using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// Server-side service that enqueues schedule invalidation notifications for affected agents.
/// When a schedule is updated or deleted, this service identifies all agents whose tags
/// match the tasks using that schedule and enqueues a <c>schedule_invalidation</c> notification
/// so they re-sync on their next heartbeat.
/// </summary>
/// <param name="notificationService">Notification outbox service.</param>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class ScheduleInvalidationDispatcher(
    AgentNotificationService notificationService,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleInvalidationDispatcher> logger
) {

    /// <summary>
    /// Notifies all affected agents that a schedule has been modified or deleted.
    /// <para>
    /// Finds tasks referencing the schedule, identifies agents whose tags overlap,
    /// and enqueues <c>schedule_invalidation</c> notifications for each. Failures are logged but do not throw.
    /// </para>
    /// </summary>
    /// <param name="scheduleId">The schedule ID that was changed.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InvalidateAsync( Guid scheduleId, CancellationToken ct = default ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Find tasks referencing this schedule to get their TargetTags
        List<WerkrTask> affectedTasks = await db.TaskSchedules
            .AsNoTracking( )
            .Where( ts => ts.ScheduleId == scheduleId )
            .Select( ts => ts.Task! )
            .ToListAsync( ct );

        // Find workflows referencing this schedule to get their TargetTags
        List<Workflow> affectedWorkflows = await db.WorkflowSchedules
            .AsNoTracking( )
            .Where( ws => ws.ScheduleId == scheduleId )
            .Select( ws => ws.Workflow! )
            .ToListAsync( ct );

        if (affectedTasks.Count == 0 && affectedWorkflows.Count == 0) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "No tasks or workflows reference schedule {ScheduleId}. Skipping invalidation.", scheduleId );
            }
            return;
        }

        // Collect all unique target tags from affected tasks and workflows
        HashSet<string> allTargetTags = new( StringComparer.OrdinalIgnoreCase );
        foreach (WerkrTask task in affectedTasks) {
            if (task.TargetTags is { Length: > 0 }) {
                foreach (string tag in task.TargetTags) {
                    string trimmed = tag.Trim();
                    if (trimmed.Length > 0) {
                        _ = allTargetTags.Add( trimmed );
                    }
                }
            }
        }
        foreach (Workflow workflow in affectedWorkflows) {
            if (workflow.TargetTags is { Length: > 0 }) {
                foreach (string tag in workflow.TargetTags) {
                    string trimmed = tag.Trim();
                    if (trimmed.Length > 0) {
                        _ = allTargetTags.Add( trimmed );
                    }
                }
            }
        }

        // Find all connected agents
        List<RegisteredConnection> agents = await db.RegisteredConnections
            .AsNoTracking( )
            .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
            .ToListAsync( ct );

        // Filter agents whose tags overlap with the affected tasks' TargetTags
        List<RegisteredConnection> affectedAgents = [];
        foreach (RegisteredConnection agent in agents) {
            string[]? agentTags = agent.Tags;
            if (agentTags is null || agentTags.Length == 0) {
                continue;
            }

            bool hasOverlap = agentTags.Any( t => allTargetTags.Contains( t ) );
            if (hasOverlap) {
                affectedAgents.Add( agent );
            }
        }

        if (affectedAgents.Count == 0) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "No agents match tags for schedule {ScheduleId}. Skipping invalidation.", scheduleId );
            }
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Enqueuing schedule invalidation for {ScheduleId} to {AgentCount} agents.",
                scheduleId, affectedAgents.Count );
        }

        // Enqueue notification for each affected agent
        foreach (RegisteredConnection agent in affectedAgents) {
            await notificationService.EnqueueAsync(
                db, agent.Id, "schedule_invalidation", scheduleId.ToString( ), ct: ct );
        }

        _ = await db.SaveChangesAsync( ct );
    }
}
