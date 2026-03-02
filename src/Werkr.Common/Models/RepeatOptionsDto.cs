namespace Werkr.Common.Models;

/// <summary>DTO for ScheduleRepeatOptions.</summary>
/// <param name="RepeatIntervalMinutes">Interval between repeats.</param>
/// <param name="RepeatDurationMinutes">Duration over which repeats occur.</param>
public sealed record RepeatOptionsDto( int RepeatIntervalMinutes, int RepeatDurationMinutes );
