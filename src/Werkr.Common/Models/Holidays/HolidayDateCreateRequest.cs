namespace Werkr.Common.Models.Holidays;

/// <summary>Request body for creating a single manual holiday date entry.</summary>
public sealed record HolidayDateCreateRequest(
    string Date,
    string Name,
    string? WindowStart,
    string? WindowEnd,
    string? WindowTimeZoneId
);
