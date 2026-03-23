using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// A notification message template for a specific event type and channel.
/// Templates use <c>{{variableName}}</c> syntax for variable interpolation.
/// Default templates ship with the platform; customized templates set <see cref="IsDefault"/> to false.
/// </summary>
[Table( "notification_templates" )]
public class NotificationTemplate : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Event type identifier, e.g. "workflow.run.failed".</summary>
    [Required]
    [MaxLength( 128 )]
    public string EventTypeId { get; set; } = string.Empty;

    /// <summary>Channel type: "email", "webhook", "inapp".</summary>
    [Required]
    [MaxLength( 32 )]
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>Email subject template (with variables). Null for non-email channels.</summary>
    [MaxLength( 500 )]
    public string? Subject { get; set; }

    /// <summary>Template body (HTML for email, JSON for webhook, text for in-app).</summary>
    [Required]
    [MaxLength( 8000 )]
    public string Body { get; set; } = string.Empty;

    /// <summary>True for shipped defaults, false for customized by admin.</summary>
    [Required]
    public bool IsDefault { get; set; } = true;

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of last modification.</summary>
    [Required]
    public DateTime ModifiedUtc { get; set; }
}
