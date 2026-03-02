using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Daily recurrence pattern for a schedule.
/// </summary>
[Table( "daily_recurrence" )]
public class DailyRecurrence : ConcurrencyBase {
    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>Recur every N days.</summary>
    public int DayInterval { get; set; } = 1;

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
