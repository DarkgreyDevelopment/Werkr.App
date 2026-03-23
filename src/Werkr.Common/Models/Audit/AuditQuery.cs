namespace Werkr.Common.Models.Audit;

/// <summary>
/// Query/filter DTO for retrieving audit events with pagination.
/// All filter fields are nullable — null means "no filter on this field".
/// </summary>
public sealed record AuditQuery {
    /// <summary>Filter by event type ID (exact match).</summary>
    public string? EventTypeId { get; init; }

    /// <summary>Filter by event category (exact match).</summary>
    public string? EventCategory { get; init; }

    /// <summary>Filter by source module (exact match).</summary>
    public string? SourceModule { get; init; }

    /// <summary>Filter by actor ID (exact match).</summary>
    public string? ActorId { get; init; }

    /// <summary>Filter by actor type (exact match).</summary>
    public string? ActorType { get; init; }

    /// <summary>Filter by entity type (exact match).</summary>
    public string? EntityType { get; init; }

    /// <summary>Filter by entity ID (exact match).</summary>
    public string? EntityId { get; init; }

    /// <summary>Filter by correlation ID (exact match).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Filter events on or after this UTC timestamp.</summary>
    public DateTime? FromUtc { get; init; }

    /// <summary>Filter events on or before this UTC timestamp.</summary>
    public DateTime? ToUtc { get; init; }

    /// <summary>Maximum number of results to return. Default 50, max 200.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Number of results to skip for pagination.</summary>
    public int Offset { get; init; }
}
