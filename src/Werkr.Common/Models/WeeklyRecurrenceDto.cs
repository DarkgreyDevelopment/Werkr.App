namespace Werkr.Common.Models;

/// <summary>DTO for WeeklyRecurrence.</summary>
/// <param name="WeekInterval">Recur every N weeks.</param>
/// <param name="DaysOfWeek">Flags value indicating which days to recur on (see <c>Werkr.Data.Calendar.Enums.DaysOfWeek</c>).</param>
public sealed record WeeklyRecurrenceDto( int WeekInterval, int DaysOfWeek );
