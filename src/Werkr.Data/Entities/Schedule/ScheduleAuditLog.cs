using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Werkr.Data.Calendar.Enums;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Records a schedule occurrence that was suppressed (or required) by a holiday calendar filter.
/// All filtered occurrences are logged for audit visibility. (Decision H10)
/// </summary>
[Table( "schedule_audit_log" )]
public class ScheduleAuditLog {
    /// <summary>Auto-incrementing primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public long Id { get; set; }

    /// <summary>FK to the schedule whose occurrence was filtered.</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>The UTC time of the occurrence that was filtered.</summary>
    public DateTime OccurrenceUtcTime { get; set; }

    /// <summary>Name of the holiday calendar that caused the filter.</summary>
    [Required]
    [MaxLength( 256 )]
    public string CalendarName { get; set; } = string.Empty;

    /// <summary>Name of the specific holiday that matched.</summary>
    [Required]
    [MaxLength( 256 )]
    public string HolidayName { get; set; } = string.Empty;

    /// <summary>Whether the calendar was operating as a blocklist or allowlist.</summary>
    public HolidayCalendarMode Mode { get; set; }

    /// <summary>UTC timestamp when this audit record was created.</summary>
    public DateTime CreatedUtc { get; set; }

    // -- Navigation properties --

    /// <summary>The schedule this audit record belongs to.</summary>
    public DbSchedule Schedule { get; set; } = null!;
}
