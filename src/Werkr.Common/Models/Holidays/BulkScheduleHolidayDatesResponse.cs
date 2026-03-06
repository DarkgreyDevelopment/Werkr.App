namespace Werkr.Common.Models.Holidays;

/// <summary>Response containing batched holiday dates computed for multiple schedules.</summary>
public sealed record BulkScheduleHolidayDatesResponse(
    IReadOnlyList<ScheduleHolidayDatesResponse> Results
);
