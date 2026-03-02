namespace Werkr.Common.Models.Holidays;

/// <summary>Attach a holiday calendar to a schedule.</summary>
public sealed record AttachHolidayCalendarRequest(
    Guid CalendarId,
    string Mode );
