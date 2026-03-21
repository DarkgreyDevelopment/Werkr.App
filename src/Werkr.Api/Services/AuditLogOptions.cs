namespace Werkr.Api.Services;

/// <summary>
/// Configuration options for audit event log retention.
/// Bound from the "AuditLog" configuration section.
/// </summary>
public sealed class AuditLogOptions {
    /// <summary>
    /// Number of days to retain audit log records before automatic cleanup.
    /// Default is 365 days per the data retention specification.
    /// </summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>
    /// Interval in minutes between retention sweep cycles.
    /// Default is 1440 (24 hours). Minimum is <see cref="MinSweepIntervalMinutes"/> to prevent excessive deletion cycles.
    /// </summary>
    public int SweepIntervalMinutes { get; set; } = 1440;

    /// <summary>
    /// Hard minimum sweep interval in minutes.
    /// </summary>
    public const int MinSweepIntervalMinutes = 15;

    /// <summary>
    /// Maximum number of rows allowed in a single export request.
    /// Default is 100,000 to prevent unbounded result sets.
    /// </summary>
    public int MaxExportRows { get; set; } = 100_000;
}
