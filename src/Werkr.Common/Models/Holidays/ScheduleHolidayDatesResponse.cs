namespace Werkr.Common.Models.Holidays;

/// <summary>
/// Response containing the holiday dates applicable to a specific schedule
/// within a given year range.
/// </summary>
public sealed record ScheduleHolidayDatesResponse(
    Guid ScheduleId,
    Guid CalendarId,
    string Mode,
    IReadOnlyList<HolidayDateDto> Dates
);
