using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// Per-user notification preferences for a specific event category.
/// Controls opted-in categories, preferred delivery channel, and quiet hours.
/// </summary>
[Table( "user_notification_preferences" )]
public class UserNotificationPreference : IKey<long> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>User ID.</summary>
    [Required]
    [MaxLength( 128 )]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Event category to configure.</summary>
    [Required]
    [MaxLength( 64 )]
    public string EventCategoryId { get; set; } = string.Empty;

    /// <summary>Preferred delivery channel type ("email", "webhook", "inapp").</summary>
    [Required]
    [MaxLength( 32 )]
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>Whether notifications for this category are enabled for this user.</summary>
    [Required]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Quiet hours start time (do not deliver during this window).</summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>Quiet hours end time.</summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>IANA timezone for quiet hours (e.g. "America/New_York").</summary>
    [MaxLength( 64 )]
    public string? QuietHoursTimezone { get; set; }
}
