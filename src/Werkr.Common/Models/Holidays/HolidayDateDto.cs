namespace Werkr.Common.Models.Holidays;

/// <summary>Full holiday date.</summary>
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
    long? GeneratedByRuleId );
