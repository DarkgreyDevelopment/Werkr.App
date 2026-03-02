namespace Werkr.Common.Models.Holidays;

/// <summary>Calendar preview response.</summary>
public sealed record HolidayPreviewResponse(
    Guid CalendarId,
    int StartYear,
    int EndYear,
    IReadOnlyList<HolidayDateDto> Dates );
