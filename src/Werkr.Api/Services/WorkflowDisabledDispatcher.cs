using Werkr.Core.Communication;
using Werkr.Data;

namespace Werkr.Api.Services;

/// <summary>
/// Server-side service that enqueues workflow-disabled notifications for connected agents.
/// When a workflow is disabled, agents with in-flight runs for that workflow should cancel them.
/// </summary>
/// <param name="notificationService">Notification outbox service.</param>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class WorkflowDisabledDispatcher(
    AgentNotificationService notificationService,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowDisabledDispatcher> logger
) {

    /// <summary>
    /// Notifies all connected agents that a workflow has been disabled.
    /// Enqueues a <c>workflow_disabled</c> notification for each agent via the outbox.
    /// </summary>
    public async Task NotifyDisabledAsync( long workflowId, CancellationToken ct = default ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Enqueuing workflow-disabled notification for WorkflowId={WorkflowId} to all connected agents.",
                workflowId );
        }

        await notificationService.EnqueueForAllAsync(
            db, "workflow_disabled", workflowId.ToString( ), ct: ct );

        _ = await db.SaveChangesAsync( ct );
    }
}
