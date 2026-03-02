namespace Werkr.Common.Models.Holidays;

/// <summary>Full holiday rule.</summary>
public sealed record HolidayRuleDto(
    long Id,
    Guid HolidayCalendarId,
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
    int? YearEnd );
