namespace Werkr.Common.Models.Holidays;

/// <summary>
/// Request body for previewing a holiday rule's materialised dates
/// over a year range without persisting.
/// </summary>
public sealed record RulePreviewRequest(
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
