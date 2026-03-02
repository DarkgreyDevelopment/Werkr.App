namespace Werkr.Common.Models.Holidays;

/// <summary>Create-date request.</summary>
public sealed record HolidayDateCreateRequest(
    string Date,
    string Name,
    string? WindowStart,
    string? WindowEnd,
    string? WindowTimeZoneId );
