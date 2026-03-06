using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Repeat options for a schedule (interval-based repetition).
/// </summary>
[Table( "schedule_repeat_options" )]
public class ScheduleRepeatOptions : ConcurrencyBase {

    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>Interval in minutes between repeat executions.</summary>
    public int RepeatIntervalMinutes { get; set; }

    /// <summary>Total duration in minutes for which repeats should occur.</summary>
    public int RepeatDurationMinutes { get; set; }

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
