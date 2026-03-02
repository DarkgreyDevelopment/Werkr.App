namespace Werkr.Api.Services;

/// <summary>
/// Configuration options for schedule audit log retention.
/// Bound from the "AuditLog" configuration section.
/// </summary>
public sealed class AuditLogOptions {
    /// <summary>
    /// Number of days to retain audit log records before automatic cleanup.
    /// Default is 90 days.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
