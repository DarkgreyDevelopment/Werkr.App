using Werkr.Common.Models.Audit;

namespace Werkr.Core.Audit;

/// <summary>
/// Extension methods for registering built-in audit event types.
/// Future modules add their own <c>RegisterXxxEvents()</c> methods following this pattern.
/// </summary>
public static class AuditEventRegistrationExtensions {
    /// <summary>
    /// Registers all core Werkr audit event types into the registry.
    /// </summary>
    public static IAuditEventTypeRegistry RegisterCoreAuditEvents( this IAuditEventTypeRegistry registry ) {
        // Security
        registry.Register( AuditEventType.AuthLoginSuccess.ToEventId( ), "Login Success", "Security", "identity" );
        registry.Register( AuditEventType.AuthLoginFailure.ToEventId( ), "Login Failure", "Security", "identity" );
        registry.Register( AuditEventType.AuthLockout.ToEventId( ), "Account Lockout", "Security", "identity" );
        registry.Register( AuditEventType.Auth2FaFailure.ToEventId( ), "2FA Failure", "Security", "identity" );
        registry.Register( AuditEventType.ApiKeyCreated.ToEventId( ), "API Key Created", "Security", "identity" );
        registry.Register( AuditEventType.ApiKeyRevoked.ToEventId( ), "API Key Revoked", "Security", "identity" );

        // User
        registry.Register( AuditEventType.UserCreated.ToEventId( ), "User Created", "User", "identity" );
        registry.Register( AuditEventType.UserUpdated.ToEventId( ), "User Updated", "User", "identity" );
        registry.Register( AuditEventType.UserDeleted.ToEventId( ), "User Deleted", "User", "identity" );
        registry.Register( AuditEventType.UserDisabled.ToEventId( ), "User Disabled", "User", "identity" );
        registry.Register( AuditEventType.UserEnabled.ToEventId( ), "User Enabled", "User", "identity" );
        registry.Register( AuditEventType.UserPasswordReset.ToEventId( ), "Password Reset", "User", "identity" );

        // Agent
        registry.Register( AuditEventType.AgentRegistered.ToEventId( ), "Agent Registered", "Agent", "core" );
        registry.Register( AuditEventType.AgentRevoked.ToEventId( ), "Agent Revoked", "Agent", "core" );
        registry.Register( AuditEventType.AgentUpdated.ToEventId( ), "Agent Updated", "Agent", "core" );
        registry.Register( AuditEventType.AgentKeyRotated.ToEventId( ), "Agent Key Rotated", "Agent", "core" );
        registry.Register( AuditEventType.AgentRegistrationCompleted.ToEventId( ), "Agent Registration Completed", "Agent", "core" );

        // Calendar
        registry.Register( AuditEventType.CalendarCreated.ToEventId( ), "Calendar Created", "Calendar", "scheduling" );
        registry.Register( AuditEventType.CalendarUpdated.ToEventId( ), "Calendar Updated", "Calendar", "scheduling" );
        registry.Register( AuditEventType.CalendarDeleted.ToEventId( ), "Calendar Deleted", "Calendar", "scheduling" );
        registry.Register( AuditEventType.CalendarCloned.ToEventId( ), "Calendar Cloned", "Calendar", "scheduling" );
        registry.Register( AuditEventType.CalendarAttached.ToEventId( ), "Calendar Attached", "Calendar", "scheduling" );
        registry.Register( AuditEventType.CalendarDetached.ToEventId( ), "Calendar Detached", "Calendar", "scheduling" );

        // Schedule
        registry.Register( AuditEventType.ScheduleOccurrenceSuppressed.ToEventId( ), "Occurrence Suppressed", "Schedule", "scheduling" );
        registry.Register( AuditEventType.ScheduleOccurrenceShifted.ToEventId( ), "Occurrence Shifted", "Schedule", "scheduling" );

        // Task
        registry.Register( AuditEventType.TaskDeleted.ToEventId( ), "Task Deleted", "Task", "core" );
        registry.Register( AuditEventType.TaskEnabled.ToEventId( ), "Task Enabled", "Task", "core" );
        registry.Register( AuditEventType.TaskDisabled.ToEventId( ), "Task Disabled", "Task", "core" );

        // Versioning
        registry.Register( AuditEventType.TaskVersionCreated.ToEventId( ), "Task Version Created", "Task", "core" );
        registry.Register( AuditEventType.WorkflowVersionCreated.ToEventId( ), "Workflow Version Created", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowVersionRollback.ToEventId( ), "Workflow Version Rollback", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowDeleted.ToEventId( ), "Workflow Deleted", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowDisabled.ToEventId( ), "Workflow Disabled", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowEnabled.ToEventId( ), "Workflow Enabled", "Workflow", "core" );
        registry.Register( AuditEventType.TriggerVersionCreated.ToEventId( ), "Trigger Version Created", "Trigger", "core" );
        registry.Register( AuditEventType.TriggerBindingUpdated.ToEventId( ), "Trigger Binding Updated", "Trigger", "core" );

        // Configuration
        registry.Register( AuditEventType.ConfigUpdated.ToEventId( ), "Config Updated", "Configuration", "core" );

        // Credential
        registry.Register( AuditEventType.CredentialCreated.ToEventId( ), "Credential Created", "Credential", "core" );
        registry.Register( AuditEventType.CredentialUpdated.ToEventId( ), "Credential Updated", "Credential", "core" );
        registry.Register( AuditEventType.CredentialRenamed.ToEventId( ), "Credential Renamed", "Credential", "core" );
        registry.Register( AuditEventType.CredentialDeleted.ToEventId( ), "Credential Deleted", "Credential", "core" );
        registry.Register( AuditEventType.CredentialScopeUpdated.ToEventId( ), "Credential Scope Updated", "Credential", "core" );
        registry.Register( AuditEventType.CredentialAccessed.ToEventId( ), "Credential Accessed", "Credential", "core" );

        // Retention
        registry.Register( AuditEventType.AuditRetentionCleanup.ToEventId( ), "Retention Cleanup", "System", "core" );
        registry.Register( AuditEventType.RetentionSweepCompleted.ToEventId( ), "Retention Sweep Completed", "System", "core" );

        // Notification
        registry.Register( AuditEventType.NotificationChannelCreated.ToEventId( ), "Channel Created", "Notification", "core" );
        registry.Register( AuditEventType.NotificationChannelUpdated.ToEventId( ), "Channel Updated", "Notification", "core" );
        registry.Register( AuditEventType.NotificationChannelDeleted.ToEventId( ), "Channel Deleted", "Notification", "core" );
        registry.Register( AuditEventType.NotificationChannelTested.ToEventId( ), "Channel Tested", "Notification", "core" );
        registry.Register( AuditEventType.NotificationSubscriptionCreated.ToEventId( ), "Subscription Created", "Notification", "core" );
        registry.Register( AuditEventType.NotificationSubscriptionUpdated.ToEventId( ), "Subscription Updated", "Notification", "core" );
        registry.Register( AuditEventType.NotificationSubscriptionDeleted.ToEventId( ), "Subscription Deleted", "Notification", "core" );
        registry.Register( AuditEventType.NotificationPreferenceUpdated.ToEventId( ), "Preference Updated", "Notification", "core" );
        registry.Register( AuditEventType.NotificationDeliverySent.ToEventId( ), "Delivery Sent", "Notification", "core" );
        registry.Register( AuditEventType.NotificationDeliveryFailed.ToEventId( ), "Delivery Failed", "Notification", "core" );
        registry.Register( AuditEventType.NotificationDeliveryDeadLettered.ToEventId( ), "Delivery Dead-Lettered", "Notification", "core" );

        return registry;
    }
}
