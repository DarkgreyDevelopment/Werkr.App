using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// A persisted in-app notification for a specific user.
/// Delivered in real-time via SignalR when the user is connected,
/// or queued for retrieval on next login.
/// </summary>
[Table( "user_notifications" )]
public class UserNotification : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Target user ID (FK to WerkrUser).</summary>
    [Required]
    [MaxLength( 128 )]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Event type identifier, e.g. "workflow.run.failed".</summary>
    [Required]
    [MaxLength( 128 )]
    public string EventTypeId { get; set; } = string.Empty;

    /// <summary>Event category identifier, e.g. "workflow_execution".</summary>
    [Required]
    [MaxLength( 64 )]
    public string EventCategoryId { get; set; } = string.Empty;

    /// <summary>Notification title.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Title { get; set; } = string.Empty;

    /// <summary>Notification body text.</summary>
    [Required]
    [MaxLength( 2000 )]
    public string Body { get; set; } = string.Empty;

    /// <summary>Whether the user has read this notification.</summary>
    [Required]
    public bool IsRead { get; set; }

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp when the notification was read.</summary>
    public DateTime? ReadUtc { get; set; }

    /// <summary>Optional deep link to the relevant page (e.g., run detail).</summary>
    [MaxLength( 512 )]
    public string? Link { get; set; }
}
