namespace Werkr.Common.Models;

/// <summary>DTO for DailyRecurrence.</summary>
/// <param name="DayInterval">Recur every N days.</param>
public sealed record DailyRecurrenceDto( int DayInterval );
