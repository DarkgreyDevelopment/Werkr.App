namespace Werkr.Common.Models.Holidays;

/// <summary>Detail of a single schedule occurrence that was suppressed by a matching holiday date.</summary>
public sealed record SuppressedOccurrenceDto(
    DateTime UtcTime,
    string HolidayName,
    string Reason
);
