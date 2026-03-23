namespace Werkr.Common.Models.Holidays;

/// <summary>Summary of a holiday calendar attached to a schedule.</summary>
public sealed record ScheduleHolidayCalendarDto(
    Guid CalendarId,
    string CalendarName,
    string Mode
);
