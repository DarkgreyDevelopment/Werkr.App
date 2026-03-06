namespace Werkr.Common.Models;

/// <summary>API transport DTO for a schedule's optional expiration date/time and time-zone.</summary>
/// <param name="Date">Expiration date.</param>
/// <param name="Time">Expiration time.</param>
/// <param name="TimeZoneId">IANA or Windows timezone identifier.</param>
public sealed record ExpirationDateTimeDto(
    DateOnly Date,
    TimeOnly Time,
    string TimeZoneId
);
