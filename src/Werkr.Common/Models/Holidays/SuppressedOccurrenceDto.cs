namespace Werkr.Common.Models.Holidays;

/// <summary>Suppressed occurrence detail.</summary>
public sealed record SuppressedOccurrenceDto(
    DateTime UtcTime,
    string HolidayName,
    string Reason );
