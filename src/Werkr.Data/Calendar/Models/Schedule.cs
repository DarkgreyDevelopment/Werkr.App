using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Data.Calendar.Models;

/// <summary>
/// Composite model that aggregates all schedule-related entities into a single in-memory view.
/// </summary>
public class Schedule {

    /// <summary>The core schedule entity.</summary>
    public required DbSchedule DbSchedule { get; set; }

    /// <summary>Start date/time/timezone information.</summary>
    public StartDateTimeInfo? StartDateTime { get; set; }

    /// <summary>Expiration date/time/timezone information.</summary>
    public ExpirationDateTimeInfo? Expiration { get; set; }

    /// <summary>Daily recurrence pattern.</summary>
    public DailyRecurrence? DailyRecurrence { get; set; }

    /// <summary>Weekly recurrence pattern.</summary>
    public WeeklyRecurrence? WeeklyRecurrence { get; set; }

    /// <summary>Monthly recurrence pattern.</summary>
    public MonthlyRecurrence? MonthlyRecurrence { get; set; }

    /// <summary>Repeat options for the schedule.</summary>
    public ScheduleRepeatOptions? RepeatOptions { get; set; }

    /// <summary>Attached holiday calendar (loaded by service layer).</summary>
    public HolidayCalendar? HolidayCalendar { get; set; }

    /// <summary>Mode of the attached holiday calendar (blocklist/allowlist).</summary>
    public HolidayCalendarMode? HolidayCalendarMode { get; set; }

    /// <summary>How occurrences on non-business days are handled.</summary>
    public ShiftMode? ShiftMode { get; set; }
}
