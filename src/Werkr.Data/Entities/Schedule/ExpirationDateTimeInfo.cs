using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Stores the expiration date, time, and timezone for a schedule.
/// Inherits Date, Time, TimeZone, TzTime, UtcTime, and concurrency
/// properties from <see cref="DateTimeInfoBase"/>.
/// </summary>
[Table( "schedule_expiration" )]
public class ExpirationDateTimeInfo : DateTimeInfoBase {
    /// <summary>Foreign key to the parent schedule.</summary>
    [Key]
    public Guid ScheduleId { get; set; }

    /// <summary>Navigation property to the parent schedule.</summary>
    [ForeignKey( nameof( ScheduleId ) )]
    public DbSchedule? Schedule { get; set; }
}
