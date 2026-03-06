namespace Werkr.Common.Models;

/// <summary>API transport DTO for a schedule's start date/time and time-zone.</summary>
/// <param name="Date">Start date.</param>
/// <param name="Time">Start time.</param>
/// <param name="TimeZoneId">IANA or Windows timezone identifier.</param>
public sealed record StartDateTimeDto(
    DateOnly Date,
    TimeOnly Time,
    string TimeZoneId
);
