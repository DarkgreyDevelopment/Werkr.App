namespace Werkr.Common.Models.Holidays;

/// <summary>Create-calendar request.</summary>
public sealed record HolidayCalendarCreateRequest(
    string Name,
    string Description );
