namespace Werkr.Common.Models.Holidays;

/// <summary>Update-rule request.</summary>
public sealed record HolidayRuleUpdateRequest(
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
