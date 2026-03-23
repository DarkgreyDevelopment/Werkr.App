namespace Werkr.Common.Models;

/// <summary>API transport DTO for a daily recurrence pattern attached to a schedule.</summary>
/// <param name="DayInterval">Recur every N days.</param>
public sealed record DailyRecurrenceDto( int DayInterval );
