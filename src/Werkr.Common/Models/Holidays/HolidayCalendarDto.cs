namespace Werkr.Common.Models.Holidays;

/// <summary>Full representation of a holiday calendar including all associated rules and materialised dates.</summary>
public sealed record HolidayCalendarDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemCalendar,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<HolidayRuleDto> Rules,
    IReadOnlyList<HolidayDateDto> Dates
);
