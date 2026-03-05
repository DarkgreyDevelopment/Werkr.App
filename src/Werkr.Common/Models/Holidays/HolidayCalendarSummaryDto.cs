namespace Werkr.Common.Models.Holidays;

/// <summary>Lightweight calendar list item with rule and date counts.</summary>
public sealed record HolidayCalendarSummaryDto(
    Guid Id,
    string Name,
    string Description,
    bool IsSystemCalendar,
    int RuleCount,
    int AttachedScheduleCount
);
