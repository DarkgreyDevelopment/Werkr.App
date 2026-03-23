using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Calendar.Enums;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Junction entity linking a <see cref="DbSchedule"/> to a <see cref="HolidayCalendar"/>
/// with a mode (allowlist or blocklist). Enforces one calendar per schedule via unique index. (Decision H5, H6)
/// </summary>
[Table( "schedule_holiday_calendars" )]
public class ScheduleHolidayCalendar {

    /// <summary>FK to the schedule.</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>FK to the holiday calendar.</summary>
    public Guid HolidayCalendarId { get; set; }

    /// <summary>Whether the calendar acts as a blocklist or allowlist for this schedule.</summary>
    public HolidayCalendarMode Mode { get; set; }

    // -- Navigation properties --

    /// <summary>The schedule this link belongs to.</summary>
    public DbSchedule Schedule { get; set; } = null!;

    /// <summary>The holiday calendar this link references.</summary>
    public HolidayCalendar Calendar { get; set; } = null!;
}
