using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Stores the start date, time, and timezone for a schedule.
/// Inherits Date, Time, TimeZone, TzTime, UtcTime, and concurrency
/// properties from <see cref="DateTimeInfoBase"/>.
/// </summary>
[Table( "schedule_start_datetimeinfo" )]
public class StartDateTimeInfo : DateTimeInfoBase {

    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
