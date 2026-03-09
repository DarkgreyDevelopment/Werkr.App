using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Many-to-many join entity linking a <see cref="WerkrTask"/> to a <see cref="DbSchedule"/>.
/// </summary>
[Table( "task_schedules" )]
public class TaskSchedule {

    /// <summary>Foreign key to the task.</summary>
    public long TaskId { get; set; }

    /// <summary>Foreign key to the schedule.</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>When this link was created (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this link represents a one-time "Run Now" schedule.
    /// Used to identify cleanup targets for expired one-time schedules.
    /// </summary>
    public bool IsOneTime { get; set; }

    /// <summary>Navigation property to the task.</summary>
    [ForeignKey( nameof( TaskId ) )]
    public WerkrTask? Task { get; set; }

    /// <summary>Navigation property to the schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
