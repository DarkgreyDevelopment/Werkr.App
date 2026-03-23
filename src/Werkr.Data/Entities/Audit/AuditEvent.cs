using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Audit;

/// <summary>
/// Append-only audit event record. Captures all auditable operations across the platform.
/// No concurrency tracking — audit events are immutable once written.
/// </summary>
[Table( "audit_events" )]
public class AuditEvent {

    /// <summary>Auto-incrementing primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Dotted event type identifier, e.g. "auth.login.success".</summary>
    [Required]
    [MaxLength( 128 )]
    public string EventTypeId { get; set; } = string.Empty;

    /// <summary>High-level category, e.g. "Security", "Task", "Agent".</summary>
    [Required]
    [MaxLength( 64 )]
    public string EventCategory { get; set; } = string.Empty;

    /// <summary>Module that originated the event, e.g. "core", "identity", "agent".</summary>
    [Required]
    [MaxLength( 64 )]
    public string SourceModule { get; set; } = string.Empty;

    /// <summary>Identifier of the actor (user ID, agent ID, etc.). Null for system events.</summary>
    [MaxLength( 128 )]
    public string? ActorId { get; set; }

    /// <summary>Type of actor that performed the action.</summary>
    public ActorType ActorType { get; set; }

    /// <summary>Type of the entity being acted upon, e.g. "User", "Agent", "Schedule".</summary>
    [MaxLength( 64 )]
    public string? EntityType { get; set; }

    /// <summary>Identifier of the entity being acted upon.</summary>
    [MaxLength( 128 )]
    public string? EntityId { get; set; }

    /// <summary>The action performed, e.g. "Created", "Revoked", "Updated".</summary>
    [Required]
    [MaxLength( 64 )]
    public string ActionPerformed { get; set; } = string.Empty;

    /// <summary>JSON payload with event-specific details. Capped at 8KB. Defaults to empty JSON object.</summary>
    [Required]
    [MaxLength( 8192 )]
    public string Details { get; set; } = "{}";

    /// <summary>UTC timestamp when the event occurred.</summary>
    [Required]
    public DateTime TimestampUtc { get; set; }

    /// <summary>Optional correlation ID for linking related events across services.</summary>
    [MaxLength( 128 )]
    public string? CorrelationId { get; set; }
}
