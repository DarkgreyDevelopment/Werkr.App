namespace Werkr.Common.Models;

/// <summary>Response DTO for a full schedule composite.</summary>
public sealed record ScheduleDto(
    Guid Id,
    string Name,
    long StopTaskAfterMinutes,
    StartDateTimeDto? StartDateTime,
    ExpirationDateTimeDto? Expiration,
    DailyRecurrenceDto? DailyRecurrence,
    WeeklyRecurrenceDto? WeeklyRecurrence,
    MonthlyRecurrenceDto? MonthlyRecurrence,
    RepeatOptionsDto? RepeatOptions );
