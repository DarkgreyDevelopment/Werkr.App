namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a new schedule.</summary>
public sealed record ScheduleCreateRequest(
    string Name,
    long StopTaskAfterMinutes,
    StartDateTimeDto StartDateTime,
    ExpirationDateTimeDto? Expiration,
    DailyRecurrenceDto? DailyRecurrence,
    WeeklyRecurrenceDto? WeeklyRecurrence,
    MonthlyRecurrenceDto? MonthlyRecurrence,
    RepeatOptionsDto? RepeatOptions
);
