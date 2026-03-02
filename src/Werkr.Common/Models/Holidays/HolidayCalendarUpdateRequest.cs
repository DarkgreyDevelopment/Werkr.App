namespace Werkr.Common.Models.Holidays;

/// <summary>Update-calendar request.</summary>
public sealed record HolidayCalendarUpdateRequest(
    string Name,
    string Description );
