namespace Werkr.Common.Models;

/// <summary>API transport DTO for a schedule's start date/time and time-zone.</summary>
/// <param name="Date">Start date.</param>
/// <param name="Time">Start time.</param>
/// <param name="TimeZoneId">IANA or Windows timezone identifier, or a fixed-offset ID (e.g. <c>UTC+5:30</c>).</param>
/// <param name="IsFixedOffset">When true, <paramref name="TimeZoneId"/> is a fixed UTC offset with no DST rules.</param>
public sealed record StartDateTimeDto(
    DateOnly Date,
    TimeOnly Time,
    string TimeZoneId,
    bool IsFixedOffset = false
);
