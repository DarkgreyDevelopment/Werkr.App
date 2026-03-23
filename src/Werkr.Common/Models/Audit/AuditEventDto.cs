namespace Werkr.Common.Models.Audit;

/// <summary>
/// Response DTO for an audit event record.
/// </summary>
public sealed record AuditEventDto(
    long Id,
    string EventTypeId,
    string EventCategory,
    string SourceModule,
    string? ActorId,
    string ActorType,
    string? EntityType,
    string? EntityId,
    string ActionPerformed,
    string? Details,
    DateTime TimestampUtc,
    string? CorrelationId
);
