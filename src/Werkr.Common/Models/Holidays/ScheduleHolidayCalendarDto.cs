namespace Werkr.Common.Models.Holidays;

/// <summary>Attached calendar info for a schedule.</summary>
public sealed record ScheduleHolidayCalendarDto(
    Guid CalendarId,
    string CalendarName,
    string Mode );
