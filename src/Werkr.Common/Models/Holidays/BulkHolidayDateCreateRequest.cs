namespace Werkr.Common.Models.Holidays;

/// <summary>Bulk create-date request.</summary>
public sealed record BulkHolidayDateCreateRequest(
    IReadOnlyList<HolidayDateCreateRequest> Dates );
