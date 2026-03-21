using System.Collections.Frozen;

namespace Werkr.Common.Models.Audit;

/// <summary>
/// Extension methods for <see cref="AuditEventType"/> providing conversion
/// between the enum and the dotted-string identifier used in the database.
/// </summary>
public static class AuditEventTypeExtensions {
    private static readonly FrozenDictionary<AuditEventType, string> s_toId =
        new Dictionary<AuditEventType, string> {
            [AuditEventType.AuthLoginSuccess] = "auth.login.success",
            [AuditEventType.AuthLoginFailure] = "auth.login.failure",
            [AuditEventType.AuthLockout] = "auth.lockout",
            [AuditEventType.Auth2FaFailure] = "auth.2fa.failure",
            [AuditEventType.ApiKeyCreated] = "apikey.created",
            [AuditEventType.ApiKeyRevoked] = "apikey.revoked",
            [AuditEventType.UserCreated] = "user.created",
            [AuditEventType.UserUpdated] = "user.updated",
            [AuditEventType.UserDeleted] = "user.deleted",
            [AuditEventType.UserDisabled] = "user.disabled",
            [AuditEventType.UserEnabled] = "user.enabled",
            [AuditEventType.UserPasswordReset] = "user.password_reset",
            [AuditEventType.AgentRegistered] = "agent.registered",
            [AuditEventType.AgentRegistrationCompleted] = "agent.registration.completed",
            [AuditEventType.AgentRevoked] = "agent.revoked",
            [AuditEventType.AgentUpdated] = "agent.updated",
            [AuditEventType.AgentKeyRotated] = "agent.key_rotated",
            [AuditEventType.CalendarCreated] = "calendar.created",
            [AuditEventType.CalendarUpdated] = "calendar.updated",
            [AuditEventType.CalendarDeleted] = "calendar.deleted",
            [AuditEventType.CalendarCloned] = "calendar.cloned",
            [AuditEventType.CalendarAttached] = "calendar.attached",
            [AuditEventType.CalendarDetached] = "calendar.detached",
            [AuditEventType.ScheduleOccurrenceSuppressed] = "schedule.occurrence.suppressed",
            [AuditEventType.ScheduleOccurrenceShifted] = "schedule.occurrence.shifted",
            [AuditEventType.TaskDeleted] = "task.deleted",
            [AuditEventType.TaskEnabled] = "task.enabled",
            [AuditEventType.TaskDisabled] = "task.disabled",
            [AuditEventType.TaskVersionCreated] = "task.version.created",
            [AuditEventType.WorkflowVersionCreated] = "workflow.version.created",
            [AuditEventType.WorkflowVersionRollback] = "workflow.version.rollback",
            [AuditEventType.WorkflowDeleted] = "workflow.deleted",
            [AuditEventType.WorkflowDisabled] = "workflow.disabled",
            [AuditEventType.WorkflowEnabled] = "workflow.enabled",
            [AuditEventType.TriggerVersionCreated] = "trigger.version.created",
            [AuditEventType.TriggerBindingUpdated] = "trigger.binding.updated",
            [AuditEventType.AuditRetentionCleanup] = "audit.retention.cleanup",
        }.ToFrozenDictionary( );

    private static readonly FrozenDictionary<string, AuditEventType> s_fromId =
        s_toId.ToFrozenDictionary( kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase );

    /// <summary>Returns the dotted-string event ID (e.g. <c>"auth.login.success"</c>).</summary>
    public static string ToEventId( this AuditEventType eventType ) =>
        s_toId.TryGetValue( eventType, out string? id )
            ? id
            : throw new ArgumentOutOfRangeException( nameof( eventType ), eventType, "Unknown audit event type." );

    /// <summary>Parses a dotted-string event ID back to the enum. Returns false if unknown.</summary>
    public static bool TryFromEventId( string eventId, out AuditEventType eventType ) =>
        s_fromId.TryGetValue( eventId, out eventType );
}
