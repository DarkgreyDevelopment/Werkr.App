using Werkr.Common.Models;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Api.Models;

/// <summary>
/// Bidirectional mapping between Schedule DTOs and domain entities.
/// </summary>
internal static class ScheduleMapper {
    /// <summary>Creates a <see cref="Schedule"/> composite from a create request.</summary>
    public static Schedule ToSchedule( ScheduleCreateRequest request ) =>
        new( ) {
            DbSchedule = new DbSchedule {
                Name = request.Name,
                StopTaskAfterMinutes = request.StopTaskAfterMinutes,
            },
            StartDateTime = ToStartDateTimeInfo( request.StartDateTime ),
            Expiration = request.Expiration is not null ? ToExpirationDateTimeInfo( request.Expiration ) : null,
            DailyRecurrence = request.DailyRecurrence is not null ? ToDailyRecurrence( request.DailyRecurrence ) : null,
            WeeklyRecurrence = request.WeeklyRecurrence is not null ? ToWeeklyRecurrence( request.WeeklyRecurrence ) : null,
            MonthlyRecurrence = request.MonthlyRecurrence is not null ? ToMonthlyRecurrence( request.MonthlyRecurrence ) : null,
            RepeatOptions = request.RepeatOptions is not null ? ToRepeatOptions( request.RepeatOptions ) : null,
        };

    /// <summary>Creates a <see cref="Schedule"/> composite from an update request, preserving the given ID.</summary>
    public static Schedule ToSchedule( Guid id, ScheduleUpdateRequest request ) =>
        new( ) {
            DbSchedule = new DbSchedule {
                Id = id,
                Name = request.Name,
                StopTaskAfterMinutes = request.StopTaskAfterMinutes,
            },
            StartDateTime = ToStartDateTimeInfo( request.StartDateTime ),
            Expiration = request.Expiration is not null ? ToExpirationDateTimeInfo( request.Expiration ) : null,
            DailyRecurrence = request.DailyRecurrence is not null ? ToDailyRecurrence( request.DailyRecurrence ) : null,
            WeeklyRecurrence = request.WeeklyRecurrence is not null ? ToWeeklyRecurrence( request.WeeklyRecurrence ) : null,
            MonthlyRecurrence = request.MonthlyRecurrence is not null ? ToMonthlyRecurrence( request.MonthlyRecurrence ) : null,
            RepeatOptions = request.RepeatOptions is not null ? ToRepeatOptions( request.RepeatOptions ) : null,
        };

    /// <summary>Converts a <see cref="Schedule"/> composite to a response DTO.</summary>
    public static ScheduleDto ToDto( Schedule schedule ) =>
        new(
            Id: schedule.DbSchedule.Id,
            Name: schedule.DbSchedule.Name,
            StopTaskAfterMinutes: schedule.DbSchedule.StopTaskAfterMinutes,
            StartDateTime: schedule.StartDateTime is not null ? ToDto( schedule.StartDateTime ) : null,
            Expiration: schedule.Expiration is not null ? ToDto( schedule.Expiration ) : null,
            DailyRecurrence: schedule.DailyRecurrence is not null ? ToDto( schedule.DailyRecurrence ) : null,
            WeeklyRecurrence: schedule.WeeklyRecurrence is not null ? ToDto( schedule.WeeklyRecurrence ) : null,
            MonthlyRecurrence: schedule.MonthlyRecurrence is not null ? ToDto( schedule.MonthlyRecurrence ) : null,
            RepeatOptions: schedule.RepeatOptions is not null ? ToDto( schedule.RepeatOptions ) : null
        );

    // -- Entity → DTO --

    private static StartDateTimeDto ToDto( StartDateTimeInfo info ) =>
        new( info.Date, info.Time, info.TimeZone.Id );

    private static ExpirationDateTimeDto ToDto( ExpirationDateTimeInfo info ) =>
        new( info.Date, info.Time, info.TimeZone.Id );

    private static DailyRecurrenceDto ToDto( DailyRecurrence recurrence ) =>
        new( recurrence.DayInterval );

    private static WeeklyRecurrenceDto ToDto( WeeklyRecurrence recurrence ) =>
        new( recurrence.WeekInterval, (int)recurrence.DaysOfWeek );

    private static MonthlyRecurrenceDto ToDto( MonthlyRecurrence recurrence ) =>
        new( recurrence.DayNumbers, (int)recurrence.MonthsOfYear, (int?)recurrence.WeekNumber, (int?)recurrence.DaysOfWeek );

    private static RepeatOptionsDto ToDto( ScheduleRepeatOptions options ) =>
        new( options.RepeatIntervalMinutes, options.RepeatDurationMinutes );

    // -- DTO → Entity --

    private static StartDateTimeInfo ToStartDateTimeInfo( StartDateTimeDto dto ) =>
        new( ) {
            Date = dto.Date,
            Time = dto.Time,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById( dto.TimeZoneId ),
        };

    private static ExpirationDateTimeInfo ToExpirationDateTimeInfo( ExpirationDateTimeDto dto ) =>
        new( ) {
            Date = dto.Date,
            Time = dto.Time,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById( dto.TimeZoneId ),
        };

    private static DailyRecurrence ToDailyRecurrence( DailyRecurrenceDto dto ) =>
        new( ) { DayInterval = dto.DayInterval };

    private static WeeklyRecurrence ToWeeklyRecurrence( WeeklyRecurrenceDto dto ) =>
        new( ) { WeekInterval = dto.WeekInterval, DaysOfWeek = (DaysOfWeek)dto.DaysOfWeek };

    private static MonthlyRecurrence ToMonthlyRecurrence( MonthlyRecurrenceDto dto ) =>
        new( ) {
            DayNumbers = dto.DayNumbers,
            MonthsOfYear = (MonthsOfYear)dto.MonthsOfYear,
            WeekNumber = (WeekNumberWithinMonth?)dto.WeekNumber,
            DaysOfWeek = (DaysOfWeek?)dto.DaysOfWeek,
        };

    private static ScheduleRepeatOptions ToRepeatOptions( RepeatOptionsDto dto ) =>
        new( ) {
            RepeatIntervalMinutes = dto.RepeatIntervalMinutes,
            RepeatDurationMinutes = dto.RepeatDurationMinutes,
        };
}
