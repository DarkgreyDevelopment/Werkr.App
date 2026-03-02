namespace Werkr.Common.Models.Holidays;

/// <summary>Batched holiday dates response.</summary>
public sealed record BulkScheduleHolidayDatesResponse(
    IReadOnlyList<ScheduleHolidayDatesResponse> Results );
