using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Werkr.Data.Calendar.Enums;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Weekly recurrence pattern for a schedule.
/// </summary>
[Table( "weekly_recurrence" )]
public class WeeklyRecurrence : ConcurrencyBase {
    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>Recur every N weeks.</summary>
    public int WeekInterval { get; set; } = 1;

    /// <summary>Flags indicating which days of the week to recur on.</summary>
    public DaysOfWeek DaysOfWeek { get; set; }

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
