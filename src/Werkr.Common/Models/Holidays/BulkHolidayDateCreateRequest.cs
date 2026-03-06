namespace Werkr.Common.Models.Holidays;

/// <summary>Request body to create multiple holiday dates in a single operation.</summary>
public sealed record BulkHolidayDateCreateRequest(
    IReadOnlyList<HolidayDateCreateRequest> Dates
);
