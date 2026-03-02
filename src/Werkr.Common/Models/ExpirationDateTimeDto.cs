namespace Werkr.Common.Models;

/// <summary>DTO for ExpirationDateTimeInfo.</summary>
/// <param name="Date">Expiration date.</param>
/// <param name="Time">Expiration time.</param>
/// <param name="TimeZoneId">IANA or Windows timezone identifier.</param>
public sealed record ExpirationDateTimeDto( DateOnly Date, TimeOnly Time, string TimeZoneId );
