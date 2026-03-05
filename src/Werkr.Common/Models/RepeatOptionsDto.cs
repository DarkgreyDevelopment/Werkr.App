namespace Werkr.Common.Models;

/// <summary>API transport DTO for the repeat interval settings of a schedule.</summary>
/// <param name="RepeatIntervalMinutes">Interval between repeats.</param>
/// <param name="RepeatDurationMinutes">Duration over which repeats occur.</param>
public sealed record RepeatOptionsDto(
    int RepeatIntervalMinutes,
    int RepeatDurationMinutes
);
