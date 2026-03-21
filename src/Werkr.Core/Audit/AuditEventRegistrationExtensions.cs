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

        // Versioning (placeholders for subplans 2.1.2-2.1.4)
        registry.Register( AuditEventType.TaskVersionCreated.ToEventId( ), "Task Version Created", "Task", "core" );
        registry.Register( AuditEventType.WorkflowVersionCreated.ToEventId( ), "Workflow Version Created", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowVersionRollback.ToEventId( ), "Workflow Version Rollback", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowDeleted.ToEventId( ), "Workflow Deleted", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowDisabled.ToEventId( ), "Workflow Disabled", "Workflow", "core" );
        registry.Register( AuditEventType.WorkflowEnabled.ToEventId( ), "Workflow Enabled", "Workflow", "core" );
        registry.Register( AuditEventType.TriggerVersionCreated.ToEventId( ), "Trigger Version Created", "Trigger", "core" );
        registry.Register( AuditEventType.TriggerBindingUpdated.ToEventId( ), "Trigger Binding Updated", "Trigger", "core" );

        // Retention
        registry.Register( AuditEventType.AuditRetentionCleanup.ToEventId( ), "Retention Cleanup", "System", "core" );

        return registry;
    }
}
