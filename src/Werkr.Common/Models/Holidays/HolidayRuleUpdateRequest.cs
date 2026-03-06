namespace Werkr.Common.Models.Holidays;

/// <summary>Request body for updating an existing holiday rule's parameters or observance settings.</summary>
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
    int? YearEnd
);
