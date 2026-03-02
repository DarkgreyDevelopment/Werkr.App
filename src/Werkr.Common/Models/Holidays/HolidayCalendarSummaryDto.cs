namespace Werkr.Common.Models.Holidays;

/// <summary>Calendar list item with counts.</summary>
public sealed record HolidayCalendarSummaryDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemCalendar,
    int RuleCount,
    int AttachedScheduleCount );
