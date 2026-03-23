namespace Werkr.Common.Models.Holidays;

/// <summary>Request body for updating an existing holiday calendar's metadata.</summary>
public sealed record HolidayCalendarUpdateRequest(
    string Name,
    string Description
);
