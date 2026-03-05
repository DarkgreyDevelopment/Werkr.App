using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Werkr.Data;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Manages materialisation and caching of <see cref="HolidayDate"/> records
/// generated from <see cref="HolidayRule"/> definitions in a <see cref="HolidayCalendar"/>.
/// </summary>
public sealed partial class HolidayDateService(
    WerkrDbContext db,
    ILogger<HolidayDateService> logger
) {

    /// <summary>
    /// Computes dates from all rules in a calendar for the given year range and upserts into <c>HolidayDates</c>.
    /// Implements merge strategy (Decision H17): when a computed date matches an existing manual entry
    /// on <c>(CalendarId, Date, WindowStart)</c>, the manual entry's <c>HolidayRuleId</c> is updated.
    /// Removes stale computed entries outside the year range.
    /// </summary>
    public async Task MaterializeDatesAsync(
        Guid calendarId,
        int startYear,
        int endYear,
        CancellationToken ct = default
    ) {
        HolidayCalendar? calendar = await db.HolidayCalendars
            .Include( c => c.Rules )
            .FirstOrDefaultAsync(
                c => c.Id == calendarId,
                ct
            );

        if (calendar is null) {
            logger.LogWarning(
                "Calendar {CalendarId} not found for materialization.",
                calendarId
            );
            return;
        }

        IReadOnlyList<HolidayDate> computed = HolidayCalculator.ComputeDatesForRange(
            calendar,
            startYear,
            endYear
        );

        // Load existing dates for the year range (both manual and rule-generated)
        List<HolidayDate> existing = await db.HolidayDates
            .Where( d => d.HolidayCalendarId == calendarId && d.Year >= startYear && d.Year <= endYear )
            .ToListAsync( ct );

        foreach (HolidayDate newDate in computed) {
            // Look for an existing entry matching (CalendarId, Date, WindowStart)
            HolidayDate? match = existing.FirstOrDefault( e =>
                e.Date == newDate.Date &&
                e.WindowStart == newDate.WindowStart );

            if (match is not null) {
                // Merge: link manual entry to the generating rule (H17)
                match.HolidayRuleId = newDate.HolidayRuleId;
                match.Name = newDate.Name;
                match.WindowEnd = newDate.WindowEnd;
                match.WindowTimeZoneId = newDate.WindowTimeZoneId;
            } else {
                // Insert new computed date
                newDate.HolidayCalendarId = calendarId;
                _ = db.HolidayDates.Add( newDate );
            }
        }

        _ = await db.SaveChangesAsync( ct );
        LogMaterialized(
            logger,
            computed.Count,
            calendarId,
            startYear,
            endYear
        );
    }

    /// <summary>
    /// Queries materialized dates for a date range. Auto-materializes if missing years are detected.
    /// </summary>
    public async Task<IReadOnlyList<HolidayDate>> GetDatesForRangeAsync(
        Guid calendarId, DateOnly start, DateOnly end, CancellationToken ct = default ) {

        int startYear = start.Year;
        int endYear = end.Year;

        // Check which years have materialized data
        List<int> materializedYears = await db.HolidayDates
            .Where( d => d.HolidayCalendarId == calendarId && d.HolidayRuleId != null
                && d.Year >= startYear && d.Year <= endYear )
            .Select( d => d.Year )
            .Distinct( )
            .ToListAsync( ct );

        // Auto-materialize missing years
        for (int year = startYear; year <= endYear; year++) {
            if (!materializedYears.Contains( year )) {
                await EnsureMaterializedAsync(
                    calendarId,
                    year,
                    ct
                );
            }
        }

        // Query all dates (manual + rule-generated) in the range
        return await db.HolidayDates
            .Where( d => d.HolidayCalendarId == calendarId
                && d.Date >= start && d.Date <= end )
            .AsNoTracking( )
            .ToListAsync( ct );
    }

    /// <summary>
    /// Deletes all rule-generated <see cref="HolidayDate"/> entries for a calendar.
    /// Manual entries (<c>HolidayRuleId == null</c>) are preserved.
    /// Called on any rule mutation to invalidate the cache.
    /// </summary>
    public async Task InvalidateCacheAsync(
        Guid calendarId,
        CancellationToken ct = default
    ) {
        int deleted = await db.HolidayDates
            .Where( d => d.HolidayCalendarId == calendarId && d.HolidayRuleId != null )
            .ExecuteDeleteAsync( ct );

        LogCacheInvalidated(
            logger,
            deleted,
            calendarId
        );
    }

    /// <summary>
    /// Materializes a single year if not already present.
    /// </summary>
    public async Task EnsureMaterializedAsync(
        Guid calendarId,
        int year,
        CancellationToken ct = default
    ) {
        bool hasData = await db.HolidayDates
            .AnyAsync( d => d.HolidayCalendarId == calendarId
                && d.HolidayRuleId != null && d.Year == year, ct );

        if (!hasData) {
            await MaterializeDatesAsync(
                calendarId,
                year,
                year,
                ct
            );
        }
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Materialized {Count} dates for calendar {CalendarId} ({StartYear}-{EndYear})" )]
    private static partial void LogMaterialized(
        ILogger logger,
        int count,
        Guid calendarId,
        int startYear,
        int endYear
    );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Invalidated {Count} cached dates for calendar {CalendarId}" )]
    private static partial void LogCacheInvalidated(
        ILogger logger,
        int count,
        Guid calendarId
    );
}
