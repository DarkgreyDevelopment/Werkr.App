namespace Werkr.Common.Models.Holidays;

/// <summary>Preview of materialised holiday dates for a calendar over a requested year range.</summary>
public sealed record HolidayPreviewResponse(
    Guid CalendarId,
    int StartYear,
    int EndYear,
    IReadOnlyList<HolidayDateDto> Dates
);
