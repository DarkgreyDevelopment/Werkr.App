namespace Werkr.Common.Models.Holidays;

/// <summary>Per-schedule holiday dates response.</summary>
public sealed record ScheduleHolidayDatesResponse(
    Guid ScheduleId,
    Guid CalendarId,
    string Mode,
    IReadOnlyList<HolidayDateDto> Dates );
