using Werkr.Common.Models.Holidays;

namespace Werkr.Common.Models;

/// <summary>Response DTO for occurrence preview.</summary>
public sealed record OccurrencePreviewResponse(
    Guid ScheduleId,
    DateTime WindowEnd,
    IReadOnlyList<DateTime> Occurrences,
    IReadOnlyList<SuppressedOccurrenceDto>? Suppressed = null,
    string? HolidayCalendarName = null,
    string? HolidayCalendarMode = null
);
