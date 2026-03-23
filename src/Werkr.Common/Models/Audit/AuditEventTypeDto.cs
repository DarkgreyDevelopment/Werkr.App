namespace Werkr.Common.Models.Audit;

/// <summary>
/// DTO for a registered audit event type definition.
/// </summary>
public sealed record AuditEventTypeDto(
    string EventTypeId,
    string DisplayName,
    string Category,
    string SourceModule
);
