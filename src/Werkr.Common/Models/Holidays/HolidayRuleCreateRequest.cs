namespace Werkr.Common.Models.Holidays;

/// <summary>Request body for adding a new holiday rule to a calendar (fixed date).</summary>
public sealed record HolidayRuleCreateRequest(
    string Name,
    string RuleType,
    int? Month,
    int? Day,
    string? DayOfWeek,
    int? WeekNumber,
    string? WindowStart,
    string? WindowEnd,
    string? WindowTimeZoneId,
    string ObservanceRule,
    int? YearStart,
    int? YearEnd
);
