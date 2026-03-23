namespace Werkr.Core.Notifications;

/// <summary>
/// Entry point for the notification delivery pipeline.
/// Evaluates subscriptions, user preferences, renders templates, and delivers.
/// Notification delivery is asynchronous and does not block the caller.
/// </summary>
public interface INotificationDeliveryService {

    /// <summary>
    /// Emits a notification event into the delivery pipeline.
    /// </summary>
    /// <param name="eventTypeId">Event type identifier, e.g. "workflow.run.failed".</param>
    /// <param name="eventCategoryId">Event category identifier, e.g. "workflow_execution".</param>
    /// <param name="variables">Template variable values for rendering.</param>
    /// <param name="workflowId">Optional workflow ID for per-workflow subscription matching.</param>
    /// <param name="workflowTags">Optional workflow tags for tag-based subscription matching.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EmitAsync(
        string eventTypeId,
        string eventCategoryId,
        Dictionary<string, string> variables,
        long? workflowId = null,
        string[]? workflowTags = null,
        CancellationToken ct = default
    );
}
