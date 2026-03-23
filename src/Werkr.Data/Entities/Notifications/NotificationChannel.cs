using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// A platform-level notification channel (email, webhook, or in-app).
/// Channels are configured once and shared across the platform.
/// The <see cref="Configuration"/> column uses field-level encryption
/// via <c>EncryptedStringConverter</c> in the DbContext.
/// </summary>
[Table( "notification_channels" )]
public class NotificationChannel : ConcurrencyBase, IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>Human-readable channel name (e.g., "Ops Team Email", "PagerDuty Webhook").</summary>
    [Required]
    [MaxLength( 128 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Channel type: "email", "webhook", "inapp".</summary>
    [Required]
    [MaxLength( 32 )]
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>
    /// Channel-specific configuration JSON, encrypted at rest.
    /// Content varies by channel type (SMTP settings, webhook URL, etc.).
    /// </summary>
    [Required]
    public string Configuration { get; set; } = string.Empty;

    /// <summary>Whether the channel is enabled for delivery.</summary>
    [Required]
    public bool IsEnabled { get; set; } = true;

    /// <summary>UTC timestamp of creation.</summary>
    [Required]
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp of last modification.</summary>
    [Required]
    public DateTime ModifiedUtc { get; set; }
}
