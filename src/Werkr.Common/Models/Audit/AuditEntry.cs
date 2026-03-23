namespace Werkr.Common.Models.Audit;

/// <summary>
/// DTO for creating a new audit event.
/// </summary>
public sealed record AuditEntry(
    string EventTypeId,
    string? ActorId,
    string ActorType,
    string? EntityType,
    string? EntityId,
    string ActionPerformed,
    object? Details = null,
    string? CorrelationId = null
);
