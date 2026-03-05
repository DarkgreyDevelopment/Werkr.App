using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Werkr.Data.Entities.Schedule;

/// <summary>
/// Abstract base for date/time/timezone composite values.
/// Extends <see cref="ConcurrencyBase"/> so that entity subclasses
/// (<see cref="StartDateTimeInfo"/>, <see cref="ExpirationDateTimeInfo"/>)
/// inherit both concurrency tracking and date/time properties.
/// </summary>
public abstract class DateTimeInfoBase : ConcurrencyBase {

    /// <summary>Date component.</summary>
    [Required]
    public DateOnly Date { get; set; }

    /// <summary>Time component.</summary>
    [Required]
    public TimeOnly Time { get; set; }

    /// <summary>Timezone for the date/time.</summary>
    [Required]
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>Gets the combined DateTime in the specified timezone.</summary>
    [NotMapped]
    public DateTime TzTime => Date.ToDateTime( Time );

    /// <summary>Gets the combined DateTime in UTC.</summary>
    [NotMapped]
    public DateTime UtcTime {
        get {
            DateTime local = Date.ToDateTime( Time );
            return TimeZoneInfo.ConvertTimeToUtc( local, TimeZone );
        }
    }

    /// <summary>
    /// Converts a UTC DateTime to the entity's configured TimeZone.
    /// Used by the scheduling algorithm to convert occurrence times back to local.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="utcDateTime"/> is not <see cref="DateTimeKind.Utc"/>.</exception>
    public DateTime ConvertToTimeZone( DateTime utcDateTime )
        => utcDateTime.Kind != DateTimeKind.Utc
            ? throw new ArgumentException( "DateTime must be in UTC.", nameof( utcDateTime ) )
            : TimeZoneInfo.ConvertTimeFromUtc( utcDateTime, TimeZone );

    /// <summary>
    /// Converts a DateTime (assumed to be in the entity's TimeZone) to UTC.
    /// If the DateTime is already UTC, returns it unchanged.
    /// </summary>
    public DateTime ConvertToUtc( DateTime localDateTime )
        => localDateTime.Kind == DateTimeKind.Utc
            ? localDateTime
            : TimeZoneInfo.ConvertTimeToUtc( localDateTime, TimeZone );
}
