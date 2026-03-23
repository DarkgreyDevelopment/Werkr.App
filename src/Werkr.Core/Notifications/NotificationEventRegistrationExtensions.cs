namespace Werkr.Core.Notifications;

/// <summary>
/// Extension methods for registering the built-in 1.0 notification event categories.
/// </summary>
public static class NotificationEventRegistrationExtensions {
    /// <summary>
    /// Registers all core Werkr notification event categories and their event types.
    /// </summary>
    public static INotificationEventCategoryRegistry RegisterCoreNotificationEvents( this INotificationEventCategoryRegistry registry ) {
        // Workflow Execution
        registry.Register( new NotificationEventCategory(
            CategoryId: "workflow_execution",
            DisplayName: "Workflow Execution",
            EventTypes: [
                new NotificationEventType( "workflow.run.started", "Workflow Run Started" ),
                new NotificationEventType( "workflow.run.completed", "Workflow Run Completed" ),
                new NotificationEventType( "workflow.run.failed", "Workflow Run Failed" ),
            ],
            DefaultSubscribed: true
        ) );

        // Approval
        registry.Register( new NotificationEventCategory(
            CategoryId: "approval",
            DisplayName: "Approval",
            EventTypes: [
                new NotificationEventType( "approval.requested", "Approval Requested" ),
                new NotificationEventType( "approval.approved", "Approval Approved" ),
                new NotificationEventType( "approval.rejected", "Approval Rejected" ),
                new NotificationEventType( "approval.timed_out", "Approval Timed Out" ),
            ],
            DefaultSubscribed: true
        ) );

        // Schedule
        registry.Register( new NotificationEventCategory(
            CategoryId: "schedule",
            DisplayName: "Schedule",
            EventTypes: [
                new NotificationEventType( "schedule.trigger.fired", "Schedule Trigger Fired" ),
                new NotificationEventType( "schedule.trigger.suppressed", "Schedule Trigger Suppressed" ),
            ],
            DefaultSubscribed: false
        ) );

        // Security
        registry.Register( new NotificationEventCategory(
            CategoryId: "security",
            DisplayName: "Security",
            EventTypes: [
                new NotificationEventType( "security.auth.failure", "Authentication Failure" ),
                new NotificationEventType( "security.authz.failure", "Authorization Failure" ),
                new NotificationEventType( "security.key.rotated", "Key Rotated" ),
            ],
            DefaultSubscribed: true
        ) );

        // System
        registry.Register( new NotificationEventCategory(
            CategoryId: "system",
            DisplayName: "System",
            EventTypes: [
                new NotificationEventType( "system.agent.online", "Agent Online" ),
                new NotificationEventType( "system.agent.offline", "Agent Offline" ),
                new NotificationEventType( "system.config.changed", "Configuration Changed" ),
            ],
            DefaultSubscribed: true
        ) );

        return registry;
    }
}
