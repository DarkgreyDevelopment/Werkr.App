namespace Werkr.Common.Models.Holidays;

/// <summary>Request body for creating a new holiday calendar.</summary>
public sealed record HolidayCalendarCreateRequest(
    string Name,
    string Description
);
