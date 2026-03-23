namespace Werkr.Common.Models.Holidays;

/// <summary>
/// Materialised holiday date record including the rule that generated it
/// and the observance-adjusted date.
/// </summary>
public sealed record HolidayDateDto(
    long Id,
    Guid HolidayCalendarId,
    string Date,
    string Name,
    int Year,
    string? WindowStart,
    string? WindowEnd,
    string? WindowTimeZoneId,
    bool IsManual,
    long? GeneratedByRuleId
);
