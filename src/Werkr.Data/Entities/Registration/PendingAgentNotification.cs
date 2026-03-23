using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Registration;

/// <summary>
/// A durable notification queued for an agent, fetched during the next heartbeat cycle.
/// Implements a transactional outbox pattern: producers write rows in the same
/// transaction as their business logic; the heartbeat response drains them.
/// </summary>
[Table( "pending_agent_notifications" )]
public class PendingAgentNotification {

    /// <summary>Auto-increment primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>The agent this notification is for. FK to RegisteredConnection.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>Navigation property to the parent connection.</summary>
    public RegisteredConnection? Connection { get; set; }

    /// <summary>Notification channel identifier, max 64 characters.</summary>
    [Required]
    [MaxLength( 64 )]
    public string Channel { get; set; } = string.Empty;

    /// <summary>Optional payload data, max 2000 characters.</summary>
    [MaxLength( 2000 )]
    public string? Payload { get; set; }

    /// <summary>When this notification was created (UTC).</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>When this notification expires and should be cleaned up (UTC).</summary>
    public DateTime ExpiresUtc { get; set; }
}
