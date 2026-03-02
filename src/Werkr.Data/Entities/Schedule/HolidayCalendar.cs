using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// A named collection of holiday rules and dates that can be attached to schedules
/// to filter occurrences as an allowlist or blocklist.
/// </summary>
[Table( "holiday_calendars" )]
public class HolidayCalendar {
    /// <summary>Unique identifier.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Display name of the calendar.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    [MaxLength( 1024 )]
    public string Description { get; set; } = string.Empty;

    /// <summary>Indicates whether this is a system-seeded calendar that cannot be modified or deleted.</summary>
    public bool IsSystemCalendar { get; set; }

    /// <summary>UTC timestamp when the calendar was created.</summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>UTC timestamp when the calendar was last updated.</summary>
    public DateTime UpdatedUtc { get; set; }

    // -- Navigation properties --

    /// <summary>Algorithmic rules that generate holiday dates for this calendar.</summary>
    public ICollection<HolidayRule> Rules { get; set; } = [];

    /// <summary>Materialized and manual holiday dates for this calendar.</summary>
    public ICollection<HolidayDate> Dates { get; set; } = [];

    /// <summary>Links to schedules that reference this calendar.</summary>
    public ICollection<ScheduleHolidayCalendar> ScheduleLinks { get; set; } = [];
}
