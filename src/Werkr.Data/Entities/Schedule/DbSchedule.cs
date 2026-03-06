using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Werkr.Data.Entities.Interfaces;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Represents a named schedule configuration.
/// </summary>
[Table( "schedules" )]
public class DbSchedule : ConcurrencyBase, IKey<Guid> {

    /// <summary>Database-generated primary key.</summary>
    [Key]
    [DatabaseGenerated( DatabaseGeneratedOption.Identity )]
    public Guid Id { get; set; }

    /// <summary>Display name of the schedule.</summary>
    [Required]
    [MaxLength( 256 )]
    public string Name { get; set; } = string.Empty;

    /// <summary>Maximum number of minutes a task should run before being stopped. 0 = no limit.</summary>
    public long StopTaskAfterMinutes { get; set; }

    /// <summary>Navigation property for start date/time info.</summary>
    public StartDateTimeInfo? StartDateTime { get; set; }

    /// <summary>Navigation property for expiration date/time info.</summary>
    public ExpirationDateTimeInfo? Expiration { get; set; }

    /// <summary>Navigation property for repeat options.</summary>
    public ScheduleRepeatOptions? RepeatOptions { get; set; }

    /// <summary>Navigation property for daily recurrence.</summary>
    public DailyRecurrence? DailyRecurrence { get; set; }

    /// <summary>Navigation property for weekly recurrence.</summary>
    public WeeklyRecurrence? WeeklyRecurrence { get; set; }

    /// <summary>Navigation property for monthly recurrence.</summary>
    public MonthlyRecurrence? MonthlyRecurrence { get; set; }

    /// <summary>Link to attached holiday calendar (if any).</summary>
    public ScheduleHolidayCalendar? HolidayCalendarLink { get; set; }
}
