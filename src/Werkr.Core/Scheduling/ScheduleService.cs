using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Data;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Calendar.Validation;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Provides CRUD operations for <see cref="Schedule"/> composite models,
/// mediating between the API layer and the underlying <see cref="WerkrDbContext"/>.
/// </summary>
public sealed partial class ScheduleService(
    WerkrDbContext dbContext,
    HolidayDateService holidayDateService,
    ILogger<ScheduleService> logger
) {
    private readonly WerkrDbContext _db = dbContext;
    private readonly HolidayDateService _holidayDateService = holidayDateService;
    private readonly ILogger<ScheduleService> _logger = logger;

    /// <summary>
    /// Creates a new schedule with all sub-entities in a single save.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when the schedule fails validation.</exception>
    public async Task<Schedule> CreateAsync( Schedule schedule, CancellationToken ct = default ) {
        ValidationResult? validation = ScheduleValidator.Validate( schedule );
        if (validation != ValidationResult.Success) {
            throw new ValidationException( validation!.ErrorMessage );
        }

        DbSchedule dbSchedule = new( ) {
            Name = schedule.DbSchedule.Name,
            StopTaskAfterMinutes = schedule.DbSchedule.StopTaskAfterMinutes,
        };
        _ = _db.Schedules.Add( dbSchedule );
        // SaveChanges to generate the Guid Id
        _ = await _db.SaveChangesAsync( ct );
        Guid id = dbSchedule.Id;

        // StartDateTime (required)
        StartDateTimeInfo startDt = schedule.StartDateTime!;
        startDt.ScheduleId = id;
        _ = _db.StartDateTimeInfos.Add( startDt );

        // Optional sub-entities
        if (schedule.Expiration is not null) {
            schedule.Expiration.ScheduleId = id;
            _ = _db.ExpirationDateTimeInfos.Add( schedule.Expiration );
        }
        if (schedule.RepeatOptions is not null) {
            schedule.RepeatOptions.ScheduleId = id;
            _ = _db.ScheduleRepeatOptions.Add( schedule.RepeatOptions );
        }
        if (schedule.DailyRecurrence is not null) {
            schedule.DailyRecurrence.ScheduleId = id;
            _ = _db.DailyRecurrences.Add( schedule.DailyRecurrence );
        }
        if (schedule.WeeklyRecurrence is not null) {
            schedule.WeeklyRecurrence.ScheduleId = id;
            _ = _db.WeeklyRecurrences.Add( schedule.WeeklyRecurrence );
        }
        if (schedule.MonthlyRecurrence is not null) {
            schedule.MonthlyRecurrence.ScheduleId = id;
            _ = _db.MonthlyRecurrences.Add( schedule.MonthlyRecurrence );
        }

        _ = await _db.SaveChangesAsync( ct );
        LogScheduleCreated( _logger, id, dbSchedule.Name );

        return (await GetByIdAsync( id, ct ))!;
    }

    /// <summary>
    /// Updates an existing schedule and its sub-entities.
    /// Handles changing recurrence type by removing old and adding new.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when the schedule fails validation.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when the schedule does not exist.</exception>
    public async Task<Schedule> UpdateAsync( Schedule schedule, CancellationToken ct = default ) {
        ValidationResult? validation = ScheduleValidator.Validate( schedule );
        if (validation != ValidationResult.Success) {
            throw new ValidationException( validation!.ErrorMessage );
        }

        Guid id = schedule.DbSchedule.Id;
        DbSchedule existing = await _db.Schedules.FindAsync( [id], ct )
            ?? throw new KeyNotFoundException( $"Schedule {id} not found." );

        // Update core properties
        existing.Name = schedule.DbSchedule.Name;
        existing.StopTaskAfterMinutes = schedule.DbSchedule.StopTaskAfterMinutes;

        // Update StartDateTime
        StartDateTimeInfo? startDt = await _db.StartDateTimeInfos.FindAsync( [id], ct );
        if (startDt is null) {
            schedule.StartDateTime!.ScheduleId = id;
            _ = _db.StartDateTimeInfos.Add( schedule.StartDateTime );
        } else {
            startDt.Date = schedule.StartDateTime!.Date;
            startDt.Time = schedule.StartDateTime.Time;
            startDt.TimeZone = schedule.StartDateTime.TimeZone;
        }

        // Update Expiration
        ExpirationDateTimeInfo? expiration = await _db.ExpirationDateTimeInfos.FindAsync( [id], ct );
        if (schedule.Expiration is not null) {
            if (expiration is null) {
                schedule.Expiration.ScheduleId = id;
                _ = _db.ExpirationDateTimeInfos.Add( schedule.Expiration );
            } else {
                expiration.Date = schedule.Expiration.Date;
                expiration.Time = schedule.Expiration.Time;
                expiration.TimeZone = schedule.Expiration.TimeZone;
            }
        } else if (expiration is not null) {
            _ = _db.ExpirationDateTimeInfos.Remove( expiration );
        }

        // Update RepeatOptions
        ScheduleRepeatOptions? repeat = await _db.ScheduleRepeatOptions.FindAsync( [id], ct );
        if (schedule.RepeatOptions is not null) {
            if (repeat is null) {
                schedule.RepeatOptions.ScheduleId = id;
                _ = _db.ScheduleRepeatOptions.Add( schedule.RepeatOptions );
            } else {
                repeat.RepeatIntervalMinutes = schedule.RepeatOptions.RepeatIntervalMinutes;
                repeat.RepeatDurationMinutes = schedule.RepeatOptions.RepeatDurationMinutes;
            }
        } else if (repeat is not null) {
            _ = _db.ScheduleRepeatOptions.Remove( repeat );
        }

        // Recurrence — remove old, add new (at most one type)
        DailyRecurrence? daily = await _db.DailyRecurrences.FindAsync( [id], ct );
        WeeklyRecurrence? weekly = await _db.WeeklyRecurrences.FindAsync( [id], ct );
        MonthlyRecurrence? monthly = await _db.MonthlyRecurrences.FindAsync( [id], ct );

        // Remove existing recurrences that differ from the incoming type
        if (daily is not null && schedule.DailyRecurrence is null) {
            _ = _db.DailyRecurrences.Remove( daily );
        }

        if (weekly is not null && schedule.WeeklyRecurrence is null) {
            _ = _db.WeeklyRecurrences.Remove( weekly );
        }

        if (monthly is not null && schedule.MonthlyRecurrence is null) {
            _ = _db.MonthlyRecurrences.Remove( monthly );
        }

        // Add or update the incoming recurrence
        if (schedule.DailyRecurrence is not null) {
            if (daily is null) {
                schedule.DailyRecurrence.ScheduleId = id;
                _ = _db.DailyRecurrences.Add( schedule.DailyRecurrence );
            } else {
                daily.DayInterval = schedule.DailyRecurrence.DayInterval;
            }
        }
        if (schedule.WeeklyRecurrence is not null) {
            if (weekly is null) {
                schedule.WeeklyRecurrence.ScheduleId = id;
                _ = _db.WeeklyRecurrences.Add( schedule.WeeklyRecurrence );
            } else {
                weekly.WeekInterval = schedule.WeeklyRecurrence.WeekInterval;
                weekly.DaysOfWeek = schedule.WeeklyRecurrence.DaysOfWeek;
            }
        }
        if (schedule.MonthlyRecurrence is not null) {
            if (monthly is null) {
                schedule.MonthlyRecurrence.ScheduleId = id;
                _ = _db.MonthlyRecurrences.Add( schedule.MonthlyRecurrence );
            } else {
                monthly.MonthsOfYear = schedule.MonthlyRecurrence.MonthsOfYear;
                monthly.DayNumbers = schedule.MonthlyRecurrence.DayNumbers;
                monthly.WeekNumber = schedule.MonthlyRecurrence.WeekNumber;
                monthly.DaysOfWeek = schedule.MonthlyRecurrence.DaysOfWeek;
            }
        }

        _ = await _db.SaveChangesAsync( ct );
        LogScheduleUpdated( _logger, id );

        return (await GetByIdAsync( id, ct ))!;
    }

    /// <summary>
    /// Deletes a schedule and all related sub-entities.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the schedule does not exist.</exception>
    public async Task DeleteAsync( Guid scheduleId, CancellationToken ct = default ) {
        DbSchedule existing = await _db.Schedules.FindAsync( [scheduleId], ct )
            ?? throw new KeyNotFoundException( $"Schedule {scheduleId} not found." );

        // Remove all sub-entities
        StartDateTimeInfo? startDt = await _db.StartDateTimeInfos.FindAsync( [scheduleId], ct );
        if (startDt is not null) {
            _ = _db.StartDateTimeInfos.Remove( startDt );
        }

        ExpirationDateTimeInfo? expiration = await _db.ExpirationDateTimeInfos.FindAsync( [scheduleId], ct );
        if (expiration is not null) {
            _ = _db.ExpirationDateTimeInfos.Remove( expiration );
        }

        ScheduleRepeatOptions? repeat = await _db.ScheduleRepeatOptions.FindAsync( [scheduleId], ct );
        if (repeat is not null) {
            _ = _db.ScheduleRepeatOptions.Remove( repeat );
        }

        DailyRecurrence? daily = await _db.DailyRecurrences.FindAsync( [scheduleId], ct );
        if (daily is not null) {
            _ = _db.DailyRecurrences.Remove( daily );
        }

        WeeklyRecurrence? weekly = await _db.WeeklyRecurrences.FindAsync( [scheduleId], ct );
        if (weekly is not null) {
            _ = _db.WeeklyRecurrences.Remove( weekly );
        }

        MonthlyRecurrence? monthly = await _db.MonthlyRecurrences.FindAsync( [scheduleId], ct );
        if (monthly is not null) {
            _ = _db.MonthlyRecurrences.Remove( monthly );
        }

        _ = _db.Schedules.Remove( existing );
        _ = await _db.SaveChangesAsync( ct );

        LogScheduleDeleted( _logger, scheduleId );
    }

    /// <summary>
    /// Loads a complete <see cref="Schedule"/> composite by ID, or returns null if not found.
    /// </summary>
    public async Task<Schedule?> GetByIdAsync( Guid scheduleId, CancellationToken ct = default ) {
        DbSchedule? dbSchedule = await _db.Schedules.FindAsync( [scheduleId], ct );
        return dbSchedule is null ? null : await BuildComposite( dbSchedule, ct );
    }

    /// <summary>
    /// Loads a schedule by name, or returns null if not found.
    /// </summary>
    public async Task<Schedule?> GetByNameAsync( string name, CancellationToken ct = default ) {
        DbSchedule? dbSchedule = await _db.Schedules
            .FirstOrDefaultAsync( s => s.Name == name, ct );
        return dbSchedule is null ? null : await BuildComposite( dbSchedule, ct );
    }

    /// <summary>
    /// Loads all schedules as composite models.
    /// </summary>
    public async Task<IReadOnlyList<Schedule>> GetAllAsync( CancellationToken ct = default ) {
        List<DbSchedule> dbSchedules = await _db.Schedules.ToListAsync( ct );
        List<Schedule> result = new( dbSchedules.Count );
        foreach (DbSchedule dbSchedule in dbSchedules) {
            result.Add( await BuildComposite( dbSchedule, ct ) );
        }
        return result;
    }

    /// <summary>
    /// Loads the schedule by ID and calculates its occurrences within the given window,
    /// applying any attached holiday calendar filter.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown when the schedule does not exist.</exception>
    public async Task<ScheduleOccurrenceResult> PreviewOccurrencesAsync(
        Guid scheduleId, DateTime windowEnd, CancellationToken ct = default
    ) {
        Schedule schedule = await GetByIdAsync( scheduleId, ct )
            ?? throw new KeyNotFoundException( $"Schedule {scheduleId} not found." );

        IReadOnlyList<HolidayDate>? holidayDates = null;
        if (schedule.HolidayCalendar is not null && schedule.StartDateTime is not null) {
            DateTime start = schedule.StartDateTime.UtcTime;
            holidayDates = await _holidayDateService.GetDatesForRangeAsync(
                schedule.HolidayCalendar.Id,
                DateOnly.FromDateTime( start ),
                DateOnly.FromDateTime( windowEnd ),
                ct );
        }

        return ScheduleCalculator.CalculateOccurrences(
            schedule, windowEnd, holidayDates, schedule.HolidayCalendarMode );
    }

    /// <summary>
    /// Assembles a <see cref="Schedule"/> composite from a <see cref="DbSchedule"/> and its sub-entities.
    /// Follows the reference code's pattern of loading each sub-entity by FK.
    /// </summary>
    private async Task<Schedule> BuildComposite( DbSchedule dbSchedule, CancellationToken ct ) {
        Guid id = dbSchedule.Id;
        Schedule schedule = new( ) {
            DbSchedule = dbSchedule,
            StartDateTime = await _db.StartDateTimeInfos.FindAsync( [id], ct ),
            Expiration = await _db.ExpirationDateTimeInfos.FindAsync( [id], ct ),
            RepeatOptions = await _db.ScheduleRepeatOptions.FindAsync( [id], ct ),
            DailyRecurrence = await _db.DailyRecurrences.FindAsync( [id], ct ),
            WeeklyRecurrence = await _db.WeeklyRecurrences.FindAsync( [id], ct ),
            MonthlyRecurrence = await _db.MonthlyRecurrences.FindAsync( [id], ct ),
        };

        // Load holiday calendar link if attached
        ScheduleHolidayCalendar? link = await _db.ScheduleHolidayCalendars
            .FirstOrDefaultAsync( shc => shc.ScheduleId == id, ct );
        if (link is not null) {
            schedule.HolidayCalendarMode = link.Mode;
            schedule.HolidayCalendar = await _db.HolidayCalendars.FindAsync( [link.HolidayCalendarId], ct );
        }

        return schedule;
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Created schedule {ScheduleId} ({Name})" )]
    private static partial void LogScheduleCreated( ILogger logger, Guid scheduleId, string name );

    [LoggerMessage( Level = LogLevel.Information, Message = "Updated schedule {ScheduleId}" )]
    private static partial void LogScheduleUpdated( ILogger logger, Guid scheduleId );

    [LoggerMessage( Level = LogLevel.Information, Message = "Deleted schedule {ScheduleId}" )]
    private static partial void LogScheduleDeleted( ILogger logger, Guid scheduleId );
}
