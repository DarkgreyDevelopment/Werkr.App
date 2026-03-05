namespace Werkr.Common.Models.Holidays;

/// <summary>Request body to attach an existing holiday calendar to a schedule for occurrence suppression.</summary>
public sealed record AttachHolidayCalendarRequest(
    Guid CalendarId,
    string Mode
);
