namespace Werkr.Common.Models.Holidays;

/// <summary>Full holiday calendar with all rules and dates.</summary>
public sealed record HolidayCalendarDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemCalendar,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<HolidayRuleDto> Rules,
    IReadOnlyList<HolidayDateDto> Dates );
