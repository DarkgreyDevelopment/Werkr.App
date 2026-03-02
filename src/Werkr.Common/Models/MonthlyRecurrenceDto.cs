namespace Werkr.Common.Models;

/// <summary>DTO for MonthlyRecurrence.</summary>
/// <param name="DayNumbers">Specific day-of-month values (null for week+day mode).</param>
/// <param name="MonthsOfYear">Flags value indicating which months (see <c>Werkr.Data.Calendar.Enums.MonthsOfYear</c>).</param>
/// <param name="WeekNumber">Week within month flags value (null for day-number mode, see <c>Werkr.Data.Calendar.Enums.WeekNumberWithinMonth</c>).</param>
/// <param name="DaysOfWeek">Day-of-week flags value (null for day-number mode, see <c>Werkr.Data.Calendar.Enums.DaysOfWeek</c>).</param>
public sealed record MonthlyRecurrenceDto(
    int[]? DayNumbers,
    int MonthsOfYear,
    int? WeekNumber,
    int? DaysOfWeek );
