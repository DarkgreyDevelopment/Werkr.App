namespace Werkr.Common.Models.Audit;

/// <summary>
/// Output format for audit event export.
/// </summary>
public enum ExportFormat {
    /// <summary>JSON array format.</summary>
    Json = 0,
    /// <summary>Comma-separated values format.</summary>
    Csv = 1
}

/// <summary>
/// Request DTO for exporting audit events. Wraps a query filter and an output format.
/// </summary>
public sealed record AuditExportRequest(
    AuditQuery Query,
    ExportFormat Format
);
