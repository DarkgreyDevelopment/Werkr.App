using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// A notification subscription that routes events to a specific channel.
/// Supports per-workflow, tag-based, and global scoping.
/// </summary>
[Table( "notification_subscriptions" )]
public class NotificationSubscription : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Subscription scope type.</summary>
    [Required]
    public NotificationSubscriptionType SubscriptionType { get; set; }

    /// <summary>Workflow ID for PerWorkflow subscriptions. Null otherwise.</summary>
    public long? WorkflowId { get; set; }

    /// <summary>Tag for TagBased subscriptions. Null otherwise.</summary>
    [MaxLength( 128 )]
    public string? Tag { get; set; }

    /// <summary>Which event category to subscribe to.</summary>
    [Required]
    [MaxLength( 64 )]
    public string EventCategoryId { get; set; } = string.Empty;

    /// <summary>Channel to deliver through.</summary>
    [Required]
    public long ChannelId { get; set; }

    /// <summary>Whether this subscription is active.</summary>
    [Required]
    public bool IsEnabled { get; set; } = true;

    /// <summary>User who created this subscription.</summary>
    [Required]
    [MaxLength( 128 )]
    public string CreatedByUserId { get; set; } = string.Empty;

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>Navigation property to the channel.</summary>
    [ForeignKey( nameof( ChannelId ) )]
    public NotificationChannel? Channel { get; set; }

    /// <summary>Navigation property to the workflow (PerWorkflow only).</summary>
    [ForeignKey( nameof( WorkflowId ) )]
    public Workflow? Workflow { get; set; }
}
