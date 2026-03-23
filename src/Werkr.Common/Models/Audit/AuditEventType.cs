namespace Werkr.Common.Models.Audit;

/// <summary>
/// All built-in audit event types. The enum is the single source of truth;
/// use <see cref="AuditEventTypeExtensions.ToEventId"/> to obtain the
/// dotted string identifier stored in the database and sent over the wire.
/// </summary>
public enum AuditEventType {
    // ── Security ──
    /// <summary>Successful authentication.</summary>
    AuthLoginSuccess,
    /// <summary>Failed authentication attempt.</summary>
    AuthLoginFailure,
    /// <summary>Account locked due to repeated failures.</summary>
    AuthLockout,
    /// <summary>Failed two-factor authentication attempt.</summary>
    Auth2FaFailure,
    /// <summary>API key created.</summary>
    ApiKeyCreated,
    /// <summary>API key revoked.</summary>
    ApiKeyRevoked,

    // ── User ──
    /// <summary>New user account created.</summary>
    UserCreated,
    /// <summary>User profile or roles updated.</summary>
    UserUpdated,
    /// <summary>User account deleted.</summary>
    UserDeleted,
    /// <summary>User account disabled.</summary>
    UserDisabled,
    /// <summary>User account enabled.</summary>
    UserEnabled,
    /// <summary>User password reset forced.</summary>
    UserPasswordReset,

    // ── Agent ──
    /// <summary>New agent registered (bundle generated).</summary>
    AgentRegistered,
    /// <summary>Agent registration completed via gRPC handshake.</summary>
    AgentRegistrationCompleted,
    /// <summary>Agent revoked.</summary>
    AgentRevoked,
    /// <summary>Agent connection details updated.</summary>
    AgentUpdated,
    /// <summary>Agent cryptographic key rotated.</summary>
    AgentKeyRotated,

    // ── Calendar ──
    /// <summary>Holiday calendar created.</summary>
    CalendarCreated,
    /// <summary>Holiday calendar updated.</summary>
    CalendarUpdated,
    /// <summary>Holiday calendar deleted.</summary>
    CalendarDeleted,
    /// <summary>Holiday calendar cloned.</summary>
    CalendarCloned,
    /// <summary>Holiday calendar attached to a schedule.</summary>
    CalendarAttached,
    /// <summary>Holiday calendar detached from a schedule.</summary>
    CalendarDetached,

    // ── Schedule ──
    /// <summary>Schedule occurrence suppressed by holiday calendar.</summary>
    ScheduleOccurrenceSuppressed,
    /// <summary>Schedule occurrence shifted by holiday calendar.</summary>
    ScheduleOccurrenceShifted,

    // ── Task ──
    /// <summary>Task deleted.</summary>
    TaskDeleted,
    /// <summary>Task enabled.</summary>
    TaskEnabled,
    /// <summary>Task disabled.</summary>
    TaskDisabled,

    // ── Versioning ──
    /// <summary>New task version created.</summary>
    TaskVersionCreated,
    /// <summary>New workflow version created.</summary>
    WorkflowVersionCreated,
    /// <summary>Workflow rolled back to a previous version.</summary>
    WorkflowVersionRollback,
    /// <summary>Workflow deleted.</summary>
    WorkflowDeleted,
    /// <summary>Workflow disabled.</summary>
    WorkflowDisabled,
    /// <summary>Workflow enabled.</summary>
    WorkflowEnabled,
    /// <summary>New trigger version created.</summary>
    TriggerVersionCreated,
    /// <summary>Trigger binding updated.</summary>
    TriggerBindingUpdated,

    // ── Configuration ──
    /// <summary>Configuration setting updated.</summary>
    ConfigUpdated,

    // ── Credential ──
    /// <summary>Credential created.</summary>
    CredentialCreated,
    /// <summary>Credential value or metadata updated.</summary>
    CredentialUpdated,
    /// <summary>Credential renamed (with cascading task reference updates).</summary>
    CredentialRenamed,
    /// <summary>Credential deleted.</summary>
    CredentialDeleted,
    /// <summary>Credential agent scope updated.</summary>
    CredentialScopeUpdated,
    /// <summary>Credential value accessed (decrypted for dispatch).</summary>
    CredentialAccessed,

    // ── Retention ──
    /// <summary>Audit retention cleanup executed.</summary>
    AuditRetentionCleanup,
    /// <summary>Retention sweep completed (manual or scheduled).</summary>
    RetentionSweepCompleted,

    // ── Notification ──
    /// <summary>Notification channel created.</summary>
    NotificationChannelCreated,
    /// <summary>Notification channel updated.</summary>
    NotificationChannelUpdated,
    /// <summary>Notification channel deleted.</summary>
    NotificationChannelDeleted,
    /// <summary>Notification channel test delivery sent.</summary>
    NotificationChannelTested,
    /// <summary>Notification subscription created.</summary>
    NotificationSubscriptionCreated,
    /// <summary>Notification subscription updated.</summary>
    NotificationSubscriptionUpdated,
    /// <summary>Notification subscription deleted.</summary>
    NotificationSubscriptionDeleted,
    /// <summary>User notification preference updated.</summary>
    NotificationPreferenceUpdated,
    /// <summary>Notification delivered successfully.</summary>
    NotificationDeliverySent,
    /// <summary>Notification delivery failed.</summary>
    NotificationDeliveryFailed,
    /// <summary>Notification delivery dead-lettered after exhausting retries.</summary>
    NotificationDeliveryDeadLettered,
}
