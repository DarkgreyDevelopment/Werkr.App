using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Validation;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Api.Services;

/// <summary>
/// CRUD service for holiday calendars, rules, manual dates, schedule attachment, and cache management.
/// Lives in <c>Werkr.Api</c> (not <c>Werkr.Core</c>) to avoid a circular dependency with
/// <see cref="ScheduleInvalidationDispatcher"/>.
/// </summary>
public sealed class HolidayCalendarService(
    WerkrDbContext db,
    HolidayDateService dateService,
    ScheduleInvalidationDispatcher invalidationDispatcher,
    ILogger<HolidayCalendarService> logger ) {

    private readonly ILogger<HolidayCalendarService> _logger = logger;

    // ── Calendar-Level Operations ──────────────────────────────────────────────

    /// <summary>Create a user-owned calendar (<c>IsSystemCalendar = false</c>).</summary>
    public async Task<HolidayCalendar> CreateAsync( HolidayCalendar calendar, CancellationToken ct = default ) {
        calendar.IsSystemCalendar = false;
        calendar.CreatedUtc = DateTime.UtcNow;
        calendar.UpdatedUtc = DateTime.UtcNow;
        _ = db.HolidayCalendars.Add( calendar );
        _ = await db.SaveChangesAsync( ct );
        return calendar;
    }

    /// <summary>Update name/description. Blocked for system calendars.</summary>
    public async Task<HolidayCalendar> UpdateAsync( Guid id, HolidayCalendar updated, CancellationToken ct = default ) {
        HolidayCalendar existing = await GetOrThrowAsync( id, ct );
        ThrowIfSystem( existing );

        existing.Name = updated.Name;
        existing.Description = updated.Description;
        existing.UpdatedUtc = DateTime.UtcNow;
        _ = await db.SaveChangesAsync( ct );
        return existing;
    }

    /// <summary>Delete calendar + cascade. Blocked for system calendars.</summary>
    public async Task DeleteAsync( Guid id, CancellationToken ct = default ) {
        HolidayCalendar existing = await GetOrThrowAsync( id, ct );
        ThrowIfSystem( existing );

        // Detach from all schedules first and dispatch invalidation
        List<ScheduleHolidayCalendar> links = await db.ScheduleHolidayCalendars
            .Where( l => l.HolidayCalendarId == id )
            .ToListAsync( ct );

        foreach (ScheduleHolidayCalendar link in links) {
            _ = Task.Run( ( ) => invalidationDispatcher.InvalidateAsync( link.ScheduleId, CancellationToken.None ), ct );
        }
        db.ScheduleHolidayCalendars.RemoveRange( links );

        _ = db.HolidayCalendars.Remove( existing );
        _ = await db.SaveChangesAsync( ct );
    }

    /// <summary>Load calendar with rules and dates.</summary>
    public async Task<HolidayCalendar?> GetByIdAsync( Guid id, CancellationToken ct = default ) =>
        await db.HolidayCalendars
            .Include( c => c.Rules )
            .Include( c => c.Dates )
            .FirstOrDefaultAsync( c => c.Id == id, ct );

    /// <summary>List all calendars with summary counts.</summary>
    public async Task<IReadOnlyList<HolidayCalendar>> GetAllAsync( CancellationToken ct = default ) =>
        await db.HolidayCalendars
            .Include( c => c.Rules )
            .Include( c => c.ScheduleLinks )
            .AsNoTracking( )
            .OrderBy( c => c.Name )
            .ToListAsync( ct );

    /// <summary>Deep-copy rules into new user-owned calendar. Materialized dates NOT cloned.</summary>
    public async Task<HolidayCalendar> CloneAsync( Guid sourceId, string newName, CancellationToken ct = default ) {
        HolidayCalendar source = await db.HolidayCalendars
            .Include( c => c.Rules )
            .FirstOrDefaultAsync( c => c.Id == sourceId, ct )
            ?? throw new KeyNotFoundException( $"Calendar {sourceId} not found." );

        HolidayCalendar clone = new( ) {
            Id = Guid.NewGuid( ),
            Name = newName,
            Description = source.Description,
            IsSystemCalendar = false,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        foreach (HolidayRule rule in source.Rules) {
            clone.Rules.Add( new HolidayRule {
                HolidayCalendarId = clone.Id,
                Name = rule.Name,
                RuleType = rule.RuleType,
                Month = rule.Month,
                Day = rule.Day,
                DayOfWeek = rule.DayOfWeek,
                WeekNumber = rule.WeekNumber,
                WindowStart = rule.WindowStart,
                WindowEnd = rule.WindowEnd,
                WindowTimeZoneId = rule.WindowTimeZoneId,
                ObservanceRule = rule.ObservanceRule,
                YearStart = rule.YearStart,
                YearEnd = rule.YearEnd,
            } );
        }

        _ = db.HolidayCalendars.Add( clone );
        _ = await db.SaveChangesAsync( ct );
        return clone;
    }

    // ── Rule Operations ────────────────────────────────────────────────────────

    /// <summary>Get a single rule by ID.</summary>
    public async Task<HolidayRule?> GetRuleAsync( Guid calendarId, long ruleId, CancellationToken ct = default ) =>
        await db.HolidayRules
            .FirstOrDefaultAsync( r => r.HolidayCalendarId == calendarId && r.Id == ruleId, ct );

    /// <summary>Get all rules for a calendar.</summary>
    public async Task<IReadOnlyList<HolidayRule>> GetRulesAsync( Guid calendarId, CancellationToken ct = default ) =>
        await db.HolidayRules
            .Where( r => r.HolidayCalendarId == calendarId )
            .AsNoTracking( )
            .OrderBy( r => r.Month )
            .ThenBy( r => r.Day )
            .ToListAsync( ct );

    /// <summary>Add rule with validation and cache invalidation. Blocked for system calendars. (H18)</summary>
    public async Task<HolidayRule> AddRuleAsync( Guid calendarId, HolidayRule rule, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        rule.HolidayCalendarId = calendarId;
        ValidationResult? result = HolidayRuleValidator.Validate( rule );
        if (result != ValidationResult.Success) {
            throw new ValidationException( result!.ErrorMessage );
        }

        _ = db.HolidayRules.Add( rule );
        _ = await db.SaveChangesAsync( ct );

        await dateService.InvalidateCacheAsync( calendarId, ct );
        await InvalidateLinkedSchedulesAsync( calendarId, ct );
        return rule;
    }

    /// <summary>Update rule with validation and cache invalidation. Blocked for system calendars. (H18)</summary>
    public async Task<HolidayRule> UpdateRuleAsync( Guid calendarId, long ruleId, HolidayRule updated, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        HolidayRule existing = await db.HolidayRules
            .FirstOrDefaultAsync( r => r.HolidayCalendarId == calendarId && r.Id == ruleId, ct )
            ?? throw new KeyNotFoundException( $"Rule {ruleId} not found in calendar {calendarId}." );

        existing.Name = updated.Name;
        existing.RuleType = updated.RuleType;
        existing.Month = updated.Month;
        existing.Day = updated.Day;
        existing.DayOfWeek = updated.DayOfWeek;
        existing.WeekNumber = updated.WeekNumber;
        existing.WindowStart = updated.WindowStart;
        existing.WindowEnd = updated.WindowEnd;
        existing.WindowTimeZoneId = updated.WindowTimeZoneId;
        existing.ObservanceRule = updated.ObservanceRule;
        existing.YearStart = updated.YearStart;
        existing.YearEnd = updated.YearEnd;

        ValidationResult? result = HolidayRuleValidator.Validate( existing );
        if (result != ValidationResult.Success) {
            throw new ValidationException( result!.ErrorMessage );
        }

        _ = await db.SaveChangesAsync( ct );

        await dateService.InvalidateCacheAsync( calendarId, ct );
        await InvalidateLinkedSchedulesAsync( calendarId, ct );
        return existing;
    }

    /// <summary>Remove rule + generated dates. Cache invalidation. Blocked for system calendars. (H18)</summary>
    public async Task RemoveRuleAsync( Guid calendarId, long ruleId, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        HolidayRule rule = await db.HolidayRules
            .FirstOrDefaultAsync( r => r.HolidayCalendarId == calendarId && r.Id == ruleId, ct )
            ?? throw new KeyNotFoundException( $"Rule {ruleId} not found in calendar {calendarId}." );

        _ = db.HolidayRules.Remove( rule );
        _ = await db.SaveChangesAsync( ct );

        await dateService.InvalidateCacheAsync( calendarId, ct );
        await InvalidateLinkedSchedulesAsync( calendarId, ct );
    }

    /// <summary>Compute dates for a rule without saving to the database.</summary>
    public static IReadOnlyList<HolidayDate> PreviewRuleAsync( HolidayRule rule, int startYear, int endYear ) {
        List<HolidayDate> results = [];
        for (int year = startYear; year <= endYear; year++) {
            results.AddRange( HolidayCalculator.ComputeDatesForYear( rule, year ) );
        }
        return results;
    }

    // ── Manual Date Operations ─────────────────────────────────────────────────

    /// <summary>Get a single manual date by ID.</summary>
    public async Task<HolidayDate?> GetManualDateAsync( Guid calendarId, long dateId, CancellationToken ct = default ) =>
        await db.HolidayDates
            .FirstOrDefaultAsync( d => d.HolidayCalendarId == calendarId && d.Id == dateId, ct );

    /// <summary>Get all manual dates (HolidayRuleId == null) for a calendar.</summary>
    public async Task<IReadOnlyList<HolidayDate>> GetManualDatesAsync( Guid calendarId, CancellationToken ct = default ) =>
        await db.HolidayDates
            .Where( d => d.HolidayCalendarId == calendarId && d.HolidayRuleId == null )
            .AsNoTracking( )
            .OrderBy( d => d.Date )
            .ToListAsync( ct );

    /// <summary>Add a manual date. Blocked for system calendars. (H18)</summary>
    public async Task<HolidayDate> AddManualDateAsync( Guid calendarId, HolidayDate date, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        date.HolidayCalendarId = calendarId;
        date.HolidayRuleId = null;
        _ = db.HolidayDates.Add( date );
        _ = await db.SaveChangesAsync( ct );

        await InvalidateLinkedSchedulesAsync( calendarId, ct );
        return date;
    }

    /// <summary>Update a manual date. Blocked for system calendars. (H18)</summary>
    public async Task<HolidayDate> UpdateManualDateAsync( Guid calendarId, long dateId, HolidayDate updated, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        HolidayDate existing = await db.HolidayDates
            .FirstOrDefaultAsync( d => d.HolidayCalendarId == calendarId && d.Id == dateId, ct )
            ?? throw new KeyNotFoundException( $"Date {dateId} not found in calendar {calendarId}." );

        existing.Date = updated.Date;
        existing.Name = updated.Name;
        existing.Year = updated.Year;
        existing.WindowStart = updated.WindowStart;
        existing.WindowEnd = updated.WindowEnd;
        existing.WindowTimeZoneId = updated.WindowTimeZoneId;
        _ = await db.SaveChangesAsync( ct );

        await InvalidateLinkedSchedulesAsync( calendarId, ct );
        return existing;
    }

    /// <summary>Remove a manual date. Blocked for system calendars. (H18)</summary>
    public async Task RemoveManualDateAsync( Guid calendarId, long dateId, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        HolidayDate date = await db.HolidayDates
            .FirstOrDefaultAsync( d => d.HolidayCalendarId == calendarId && d.Id == dateId, ct )
            ?? throw new KeyNotFoundException( $"Date {dateId} not found in calendar {calendarId}." );

        _ = db.HolidayDates.Remove( date );
        _ = await db.SaveChangesAsync( ct );

        await InvalidateLinkedSchedulesAsync( calendarId, ct );
    }

    /// <summary>Bulk add manual dates. Blocked for system calendars. (H18)</summary>
    public async Task<IReadOnlyList<HolidayDate>> BulkAddManualDatesAsync(
        Guid calendarId, IReadOnlyList<HolidayDate> dates, CancellationToken ct = default ) {
        HolidayCalendar calendar = await GetOrThrowAsync( calendarId, ct );
        ThrowIfSystem( calendar );

        foreach (HolidayDate date in dates) {
            date.HolidayCalendarId = calendarId;
            date.HolidayRuleId = null;
            _ = db.HolidayDates.Add( date );
        }
        _ = await db.SaveChangesAsync( ct );

        await InvalidateLinkedSchedulesAsync( calendarId, ct );
        return dates;
    }

    // ── Preview & Materialization ──────────────────────────────────────────────

    /// <summary>Compute all dates (rules + manual) for a year range without persisting.</summary>
    public async Task<IReadOnlyList<HolidayDate>> PreviewDatesAsync(
        Guid calendarId, int startYear, int endYear, CancellationToken ct = default ) {

        HolidayCalendar calendar = await db.HolidayCalendars
            .Include( c => c.Rules )
            .Include( c => c.Dates.Where( d => d.HolidayRuleId == null ) )
            .FirstOrDefaultAsync( c => c.Id == calendarId, ct )
            ?? throw new KeyNotFoundException( $"Calendar {calendarId} not found." );

        List<HolidayDate> results = [];

        // Computed from rules
        results.AddRange( HolidayCalculator.ComputeDatesForRange( calendar, startYear, endYear ) );

        // Manual dates in range
        results.AddRange( calendar.Dates.Where( d =>
            d.Year >= startYear && d.Year <= endYear ) );

        return [.. results.OrderBy( d => d.Date ).ThenBy( d => d.WindowStart )];
    }

    /// <summary>Delegates to <see cref="HolidayDateService.MaterializeDatesAsync"/>.</summary>
    public Task MaterializeDatesAsync( Guid calendarId, int startYear, int endYear, CancellationToken ct = default ) =>
        dateService.MaterializeDatesAsync( calendarId, startYear, endYear, ct );

    // ── Schedule Attachment ────────────────────────────────────────────────────

    /// <summary>Attach a calendar to a schedule. Fails if already attached (H5). Dispatches invalidation (H18).</summary>
    public async Task<ScheduleHolidayCalendar> AttachToScheduleAsync(
        Guid scheduleId, Guid calendarId, HolidayCalendarMode mode, CancellationToken ct = default ) {

        // Verify calendar exists
        bool calendarExists = await db.HolidayCalendars.AnyAsync( c => c.Id == calendarId, ct );
        if (!calendarExists) {
            throw new KeyNotFoundException( $"Calendar {calendarId} not found." );
        }

        // Verify schedule exists
        bool scheduleExists = await db.Schedules.AnyAsync( s => s.Id == scheduleId, ct );
        if (!scheduleExists) {
            throw new KeyNotFoundException( $"Schedule {scheduleId} not found." );
        }

        ScheduleHolidayCalendar link = new( ) {
            ScheduleId = scheduleId,
            HolidayCalendarId = calendarId,
            Mode = mode,
        };

        _ = db.ScheduleHolidayCalendars.Add( link );
        _ = await db.SaveChangesAsync( ct );

        _ = Task.Run( ( ) => invalidationDispatcher.InvalidateAsync( scheduleId, CancellationToken.None ), ct );
        return link;
    }

    /// <summary>Detach calendar from a schedule. Dispatches invalidation (H18).</summary>
    public async Task DetachFromScheduleAsync( Guid scheduleId, CancellationToken ct = default ) {
        ScheduleHolidayCalendar? link = await db.ScheduleHolidayCalendars
            .FirstOrDefaultAsync( l => l.ScheduleId == scheduleId, ct );

        if (link is null) {
            return;
        }

        _ = db.ScheduleHolidayCalendars.Remove( link );
        _ = await db.SaveChangesAsync( ct );

        _ = Task.Run( ( ) => invalidationDispatcher.InvalidateAsync( scheduleId, CancellationToken.None ), ct );
    }

    /// <summary>Get attached calendar info + mode for a schedule. Returns null if none.</summary>
    public async Task<ScheduleHolidayCalendar?> GetScheduleCalendarAsync( Guid scheduleId, CancellationToken ct = default ) =>
        await db.ScheduleHolidayCalendars
            .Include( l => l.Calendar )
            .FirstOrDefaultAsync( l => l.ScheduleId == scheduleId, ct );

    // ── Private Helpers ────────────────────────────────────────────────────────

    private async Task<HolidayCalendar> GetOrThrowAsync( Guid id, CancellationToken ct ) =>
        await db.HolidayCalendars.FindAsync( [id], ct )
            ?? throw new KeyNotFoundException( $"Calendar {id} not found." );

    private static void ThrowIfSystem( HolidayCalendar calendar ) {
        if (calendar.IsSystemCalendar) {
            throw new InvalidOperationException( $"Calendar '{calendar.Name}' is a system calendar and cannot be modified." );
        }
    }

    /// <summary>
    /// Decision H18: Queries schedule links for the affected calendar and dispatches
    /// invalidation for each linked schedule via fire-and-forget.
    /// </summary>
    private async Task InvalidateLinkedSchedulesAsync( Guid calendarId, CancellationToken ct ) {
        List<Guid> scheduleIds = await db.ScheduleHolidayCalendars
            .Where( l => l.HolidayCalendarId == calendarId )
            .Select( l => l.ScheduleId )
            .ToListAsync( ct );

        foreach (Guid scheduleId in scheduleIds) {
            _ = Task.Run( ( ) => invalidationDispatcher.InvalidateAsync( scheduleId, CancellationToken.None ), ct );
        }
    }
}
