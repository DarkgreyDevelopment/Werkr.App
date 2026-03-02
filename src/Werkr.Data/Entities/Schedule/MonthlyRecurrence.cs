using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Werkr.Data.Calendar.Enums;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Monthly recurrence pattern for a schedule.
/// Supports two modes:
///   1. Day-of-month: <see cref="DayNumbers"/> specifies specific days (1-31, negative for end-of-month offset).
///   2. Week-based: <see cref="WeekNumber"/> + <see cref="DaysOfWeek"/> specifies e.g. "2nd Tuesday".
/// <see cref="MonthsOfYear"/> controls which months the pattern applies to in both modes.
/// </summary>
[Table( "monthly_recurrence" )]
public class MonthlyRecurrence : ConcurrencyBase {
    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>
    /// Specific days of the month (1-31) to recur on.
    /// Use negative values for counting from end of month (-1 = last day).
    /// Null when using week-based recurrence mode.
    /// Stored as JSON in the database.
    /// </summary>
    public int[]? DayNumbers { get; set; }

    /// <summary>Flags indicating which months to recur in.</summary>
    public MonthsOfYear MonthsOfYear { get; set; }

    /// <summary>
    /// Flags indicating which weeks within the month to recur (for week-based monthly schedules).
    /// Null when using day-of-month recurrence mode.
    /// </summary>
    public WeekNumberWithinMonth? WeekNumber { get; set; }

    /// <summary>
    /// Days of week (for week-based monthly schedules).
    /// Null when using day-of-month recurrence mode.
    /// </summary>
    public DaysOfWeek? DaysOfWeek { get; set; }

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
