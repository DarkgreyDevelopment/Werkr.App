namespace Werkr.Common.Models;

/// <summary>Request DTO for updating an existing schedule.</summary>
public sealed record ScheduleUpdateRequest(
    string Name,
    long StopTaskAfterMinutes,
    StartDateTimeDto StartDateTime,
    ExpirationDateTimeDto? Expiration,
    DailyRecurrenceDto? DailyRecurrence,
    WeeklyRecurrenceDto? WeeklyRecurrence,
    MonthlyRecurrenceDto? MonthlyRecurrence,
    RepeatOptionsDto? RepeatOptions
);
