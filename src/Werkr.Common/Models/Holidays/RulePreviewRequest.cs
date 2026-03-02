namespace Werkr.Common.Models.Holidays;

/// <summary>Rule preview request (without persisting).</summary>
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
    int? YearEnd );
