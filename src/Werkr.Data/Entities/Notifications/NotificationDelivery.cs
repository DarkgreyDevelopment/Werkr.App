using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// Tracks the delivery lifecycle of a single notification.
/// Persisted in the database so the retry queue survives service restarts.
/// </summary>
[Table( "notification_deliveries" )]
public class NotificationDelivery : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Channel used for delivery.</summary>
    [Required]
    public long ChannelId { get; set; }

    /// <summary>Event type identifier.</summary>
    [Required]
    [MaxLength( 128 )]
    public string EventTypeId { get; set; } = string.Empty;

    /// <summary>Recipient: user ID or webhook URL.</summary>
    [Required]
    [MaxLength( 256 )]
    public string RecipientId { get; set; } = string.Empty;

    /// <summary>Current delivery status.</summary>
    [Required]
    public DeliveryStatus Status { get; set; }

    /// <summary>Number of delivery attempts made.</summary>
    [Required]
    public int AttemptCount { get; set; }

    /// <summary>Maximum delivery attempts before dead-lettering.</summary>
    [Required]
    public int MaxAttempts { get; set; }

    /// <summary>UTC timestamp of the last delivery attempt.</summary>
    public DateTime? LastAttemptUtc { get; set; }

    /// <summary>UTC timestamp for the next retry attempt.</summary>
    public DateTime? NextRetryUtc { get; set; }

    /// <summary>Error message from the last failed attempt.</summary>
    [MaxLength( 2000 )]
    public string? ErrorMessage { get; set; }

    /// <summary>Serialized delivery request JSON (for retry).</summary>
    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp when delivery was completed or dead-lettered.</summary>
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Navigation property to the channel.</summary>
    [ForeignKey( nameof( ChannelId ) )]
    public NotificationChannel? Channel { get; set; }
}
