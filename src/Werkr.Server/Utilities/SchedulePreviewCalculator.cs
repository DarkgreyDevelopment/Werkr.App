using Werkr.Common.Models;
using Werkr.Data;

namespace Werkr.Server.Utilities;

/// <summary>
/// Produces a preview list of upcoming UTC occurrences for a schedule definition.
/// Handles daily, weekly, and monthly recurrence patterns as well as
/// intra-day repeat windows.
/// </summary>
internal static class SchedulePreviewCalculator {
    /// <summary>
    /// Calculate the next <paramref name="count"/> occurrences for a schedule.
    /// </summary>
    /// <param name="request">The schedule definition.</param>
    /// <param name="count">Maximum number of occurrences to return.</param>
    /// <returns>A list of UTC occurrence date-times.</returns>
    internal static List<DateTime> Calculate( ScheduleCreateRequest request, int count = 10 ) {
        List<DateTime> results = [];
        if (count <= 0) {
            return results;
        }

        TimeZoneInfo tz = GetTimeZone( request.StartDateTime.TimeZoneId );
        DateTime startLocal = request.StartDateTime.Date.ToDateTime( request.StartDateTime.Time );
        DateTime startUtc = TimeZoneInfo.ConvertTimeToUtc( startLocal, tz );

        DateTime? expirationUtc = null;
        if (request.Expiration is not null) {
            DateTime expLocal = request.Expiration.Date.ToDateTime( request.Expiration.Time );
            TimeZoneInfo expTz = GetTimeZone( request.Expiration.TimeZoneId );
            expirationUtc = TimeZoneInfo.ConvertTimeToUtc( expLocal, expTz );
        }

        // Window limit — 1 year from now max
        DateTime windowEnd = DateTime.UtcNow.AddDays( 365 );
        if (expirationUtc.HasValue && expirationUtc.Value < windowEnd) {
            windowEnd = expirationUtc.Value;
        }

        DateTime cursor = startUtc;

        // One-time schedule
        if (request.DailyRecurrence is null && request.WeeklyRecurrence is null && request.MonthlyRecurrence is null) {
            if (cursor <= windowEnd) {
                results.Add( cursor );
                AddRepeatOccurrences( results, cursor, request.RepeatOptions, windowEnd, count );
            }
            return results;
        }

        while (cursor <= windowEnd && results.Count < count) {
            if (cursor >= DateTime.UtcNow) {
                results.Add( cursor );
                AddRepeatOccurrences( results, cursor, request.RepeatOptions, windowEnd, count );
                if (results.Count >= count) {
                    break;
                }
            }

            cursor = AdvanceCursor( cursor, tz, request );
            if (cursor > windowEnd) {
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Appends intra-day repeat occurrences following a base occurrence
    /// for up to <c>RepeatDurationMinutes</c>.
    /// </summary>
    private static void AddRepeatOccurrences(
        List<DateTime> results, DateTime baseOccurrence,
        RepeatOptionsDto? repeatOptions, DateTime windowEnd, int maxCount
    ) {
        if (repeatOptions is null || repeatOptions.RepeatIntervalMinutes <= 0) {
            return;
        }

        int durationMinutes = repeatOptions.RepeatDurationMinutes;
        // -1 means indefinite — cap at 24 hours for preview safety
        if (durationMinutes < 0) {
            durationMinutes = 1440;
        }

        if (durationMinutes <= 0) {
            return;
        }

        DateTime repeatEnd = baseOccurrence.AddMinutes( durationMinutes );
        if (repeatEnd > windowEnd) {
            repeatEnd = windowEnd;
        }

        DateTime repeatCursor = baseOccurrence.AddMinutes( repeatOptions.RepeatIntervalMinutes );
        while (repeatCursor <= repeatEnd && results.Count < maxCount) {
            results.Add( repeatCursor );
            repeatCursor = repeatCursor.AddMinutes( repeatOptions.RepeatIntervalMinutes );
        }
    }

    /// <summary>
    /// Advances the UTC cursor to the next primary occurrence based on the active
    /// recurrence type (daily, weekly, or monthly).
    /// </summary>
    private static DateTime AdvanceCursor( DateTime cursorUtc, TimeZoneInfo tz, ScheduleCreateRequest req ) {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc( cursorUtc, tz );

        if (req.DailyRecurrence is not null) {
            local = local.AddDays( Math.Max( 1, req.DailyRecurrence.DayInterval ) );
        } else if (req.WeeklyRecurrence is not null) {
            local = AdvanceWeekly( local, req.WeeklyRecurrence );
        } else if (req.MonthlyRecurrence is not null) {
            local = AdvanceMonthly( local, req.MonthlyRecurrence );
        } else {
            // Should not reach here — treat as one-time, push past window
            return DateTime.MaxValue;
        }

        return TimeZoneInfo.ConvertTimeToUtc( local, tz );
    }

    /// <summary>
    /// Advances the local-time cursor to the next weekly occurrence
    /// based on the selected days-of-week bitmask and the configured week interval.
    /// </summary>
    private static DateTime AdvanceWeekly( DateTime local, WeeklyRecurrenceDto weekly ) {
        // Try next day-of-week in current week, otherwise jump to next N-week cycle
        DateTime next = local.AddDays( 1 );
        for (int i = 0; i < 7; i++) {
            int flag = DayOfWeekToFlag( next.DayOfWeek );
            if ((weekly.DaysOfWeek & flag) != 0) {
                return next;
            }
            next = next.AddDays( 1 );
        }

        // Jump by interval weeks from the start of current week
        int interval = Math.Max( 1, weekly.WeekInterval );
        int daysToStartOfWeek = (  local.DayOfWeek == 0 ) ? 0 : (int) local.DayOfWeek;
        DateTime weekStart = local.AddDays( -daysToStartOfWeek ).AddDays( interval * 7 );

        for (int i = 0; i < 7; i++) {
            int flag = DayOfWeekToFlag( weekStart.AddDays( i ).DayOfWeek );
            if ((weekly.DaysOfWeek & flag) != 0) {
                return weekStart.AddDays( i );
            }
        }

        return local.AddDays( interval * 7 );
    }

    /// <summary>
    /// Advances the local-time cursor to the next monthly occurrence, supporting
    /// both day-number and week-number + day-of-week modes.
    /// </summary>
    private static DateTime AdvanceMonthly( DateTime local, MonthlyRecurrenceDto monthly ) {
        // Week+Day mode: find Nth weekday of matching month
        if (monthly.WeekNumber is not null && monthly.DaysOfWeek is not null) {
            return AdvanceMonthlyWeekAndDay( local, monthly );
        }

        // DayNumbers mode: advance to next matching month + day
        DateTime next = local.AddMonths( 1 );
        for (int attempt = 0; attempt < 24; attempt++) {
            int monthFlag = 1 << ( next.Month - 1 );
            if ((monthly.MonthsOfYear & monthFlag) != 0) {
                if (monthly.DayNumbers is { Length: > 0 }) {
                    foreach (int rawDay in monthly.DayNumbers.Order( )) {
                        int day = ResolveDayNumber( rawDay, next.Year, next.Month );
                        if (day >= 1 && day <= DateTime.DaysInMonth( next.Year, next.Month )) {
                            return new DateTime( next.Year, next.Month, day, next.Hour, next.Minute, next.Second, next.Kind );
                        }
                    }
                } else {
                    return new DateTime( next.Year, next.Month, 1, next.Hour, next.Minute, next.Second, next.Kind );
                }
            }
            next = next.AddMonths( 1 );
        }

        return local.AddYears( 2 ); // fallback
    }

    /// <summary>
    /// Advances the local-time cursor to the next matching month for monthly recurrences that use a week-number + day-of-week combination (e.g. "second Tuesday of the month").
    /// </summary>
    private static DateTime AdvanceMonthlyWeekAndDay( DateTime local, MonthlyRecurrenceDto monthly ) {
        DateTime next = local.AddMonths( 1 );
        for (int attempt = 0; attempt < 24; attempt++) {
            int monthFlag = 1 << ( next.Month - 1 );
            if ((monthly.MonthsOfYear & monthFlag) != 0) {
                DateTime? result = FindWeekAndDayInMonth(
                    next.Year, next.Month, next.Hour, next.Minute, next.Second,
                    monthly.WeekNumber!.Value, monthly.DaysOfWeek!.Value, next.Kind );
                if (result.HasValue) {
                    return result.Value;
                }
            }
            next = next.AddMonths( 1 );
        }
        return local.AddYears( 2 ); // fallback
    }

    /// <summary>
    /// Finds the first occurrence matching the WeekNumber + DaysOfWeek pattern in a given month.
    /// </summary>
    private static DateTime? FindWeekAndDayInMonth(
        int year, int month, int hour, int minute, int second,
        int weekNumberFlags, int daysOfWeekFlags, DateTimeKind kind
    ) {
        int daysInMonth = DateTime.DaysInMonth( year, month );
        _ = new DateTime( year, month, 1 );
        // Build a list of (weekNumber, dayOfWeek, dayOfMonth) for each day
        // Week 1 = days 1–7, Week 2 = days 8–14, etc.
        for (int day = 1; day <= daysInMonth; day++) {
            DateTime d = new( year, month, day );
            int weekNum = (( day - 1 ) / 7) + 1; // 1-based week number
            int weekFlag = 1 << ( weekNum - 1 );
            int dayFlag = DayOfWeekToFlag( d.DayOfWeek );

            if ((weekNumberFlags & weekFlag) != 0 && (daysOfWeekFlags & dayFlag) != 0) {
                return new DateTime( year, month, day, hour, minute, second, kind );
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a day number, handling negative values (count from end of month).
    /// </summary>
    private static int ResolveDayNumber( int rawDay, int year, int month ) {
        if (rawDay > 0) {
            return rawDay;
        }

        if (rawDay < 0) {
            return DateTime.DaysInMonth( year, month ) + rawDay + 1;
        }

        return 0; // 0 is invalid
    }

    /// <summary>
    /// Converts a <see cref="DayOfWeek"/> value to a single-bit flag suitable for comparison against the bitmask format used by <see cref="WeeklyRecurrenceDto"/> and <see cref="MonthlyRecurrenceDto"/> (Sunday = 1, Monday = 2, Tuesday = 4, …, Saturday = 64).
    /// </summary>
    private static int DayOfWeekToFlag( DayOfWeek day ) => day switch {
        DayOfWeek.Sunday => 1,
        DayOfWeek.Monday => 2,
        DayOfWeek.Tuesday => 4,
        DayOfWeek.Wednesday => 8,
        DayOfWeek.Thursday => 16,
        DayOfWeek.Friday => 32,
        DayOfWeek.Saturday => 64,
        _ => 0
    };

    /// <summary>
    /// Resolves a time-zone identifier string to a <see cref="TimeZoneInfo"/> instance. Falls back to <see cref="TimeZoneInfo.Utc"/> when the identifier is not found on the current system.
    /// </summary>
    private static TimeZoneInfo GetTimeZone( string timeZoneId ) {
        try {
            return TimeZoneResolver.FindOrCreate( timeZoneId );
        } catch {
            return TimeZoneInfo.Utc;
        }
    }
}
