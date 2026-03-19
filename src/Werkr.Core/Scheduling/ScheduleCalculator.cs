using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Collections;
using Werkr.Data.Entities.Schedule;

using HolidayCalendarMode = Werkr.Data.Calendar.Enums.HolidayCalendarMode;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Calculates schedule occurrence DateTimes from a <see cref="Schedule"/> composite model.
/// Ported from the WerkrDotApp reference ScheduleExtensions with name mappings applied.
/// </summary>
public static class ScheduleCalculator {

    #region Calculate Occurrences

    /// <summary>
    /// Calculates the occurrences of this schedule starting from the <see cref="StartDateTimeInfo"/> until the
    /// <paramref name="endOfWindow"/> DateTime (or the occurrences reach the schedule Expiration.UtcTime).
    /// </summary>
    /// <returns>An ordered read-only list containing all of the DateTimes that the schedule should run within the
    /// window.</returns>
    public static IReadOnlyList<DateTime> CalculateOccurrences(
        Schedule schedule,
        DateTime endOfWindow
    ) {
        HashSet<DateTime> result = [];

        if (endOfWindow.Kind != DateTimeKind.Utc) {
            throw new InvalidOperationException( "The endOfWindow DateTime must be in UTC." );
        }

        StartDateTimeInfo startDt = schedule.StartDateTime
            ?? throw new InvalidOperationException( "Schedule must have a StartDateTime." );

        DateTime occurrence = startDt.UtcTime;
        TimeOnly startTime = TimeOnly.FromDateTime( occurrence );

        // Add initial StartDateTime UtcTime to result list (if not past expiration)
        // then loop through and add any repeat occurrences.
        if (AddToResultAndCalculateRepeatOccurrences(
            occurrence,
            startDt,
            schedule.RepeatOptions,
            schedule.Expiration,
            endOfWindow,
            result
        )) { return result.OrderBy( dt => dt ).ToList( ).AsReadOnly( ); }

        // Calculate Recurrence Schedules
        if (schedule.DailyRecurrence != null) {
            CalculateDailyOccurrences(
                startDt,
                schedule.RepeatOptions,
                schedule.Expiration,
                endOfWindow,
                result,
                schedule.DailyRecurrence
            );
        } else if (schedule.WeeklyRecurrence != null) {
            CalculateWeeklyOccurrences(
                startDt,
                schedule.RepeatOptions,
                schedule.Expiration,
                endOfWindow,
                result,
                schedule.WeeklyRecurrence
            );
        } else if (schedule.MonthlyRecurrence != null) {
            CalculateMonthlyOccurrences(
                occurrence,
                startDt,
                schedule.RepeatOptions,
                schedule.Expiration,
                endOfWindow,
                result,
                schedule.MonthlyRecurrence,
                startTime
            );
        }

        return result.OrderBy( dt => dt ).ToList( ).AsReadOnly( );
    }

    /// <summary>
    /// <see cref="DateTimeOffset"/> overload for <see cref="CalculateOccurrences(Schedule, DateTime)"/>.
    /// Converts the <paramref name="endOfWindow"/> to UTC DateTime internally, then wraps
    /// each resulting occurrence as a <see cref="DateTimeOffset"/> in UTC.
    /// </summary>
    /// <param name="schedule">The schedule composite model.</param>
    /// <param name="endOfWindow">End of the preview window.</param>
    /// <returns>An ordered read-only list of occurrence times as <see cref="DateTimeOffset"/> (UTC).</returns>
    public static IReadOnlyList<DateTimeOffset> CalculateOccurrences(
        Schedule schedule,
        DateTimeOffset endOfWindow
    ) {
        IReadOnlyList<DateTime> utcOccurrences = CalculateOccurrences(
            schedule,
            endOfWindow.UtcDateTime
        );
        return utcOccurrences
            .Select( dt => new DateTimeOffset(
                dt,
                TimeSpan.Zero
            ) )
            .ToList( )
            .AsReadOnly( );
    }

    /// <summary>
    /// Holiday-aware overload. Computes raw occurrences via the existing recurrence algorithms,
    /// then applies a post-filter using the supplied holiday dates (Decision H11).
    /// </summary>
    /// <param name="schedule">The schedule composite model.</param>
    /// <param name="endOfWindow">End of the preview window (UTC).</param>
    /// <param name="holidayDates">Pre-materialized holiday dates to filter against, or null for no filtering.</param>
    /// <param name="mode">Blocklist (suppress matches) or Allowlist (keep only matches), or null for no
    /// filtering.</param>
    /// <returns>A <see cref="ScheduleOccurrenceResult"/> with both kept and suppressed occurrences.</returns>
    public static ScheduleOccurrenceResult CalculateOccurrences(
        Schedule schedule,
        DateTime endOfWindow,
        IReadOnlyList<HolidayDate>? holidayDates,
        HolidayCalendarMode? mode
    ) {

        IReadOnlyList<DateTime> rawOccurrences = CalculateOccurrences(
            schedule,
            endOfWindow
        );

        if (holidayDates is null || !holidayDates.Any( ) || mode is null) {
            return new ScheduleOccurrenceResult(
                rawOccurrences,
                []
            );
        }

        List<DateTime> kept = [];
        List<SuppressedOccurrence> suppressed = [];

        foreach (DateTime occ in rawOccurrences) {
            HolidayDate? matchingHoliday = holidayDates.FirstOrDefault( h => IsOccurrenceOnHoliday(
                occ,
                h
            ) );

            if (mode == HolidayCalendarMode.Blocklist) {
                if (matchingHoliday is not null) {
                    suppressed.Add( new SuppressedOccurrence( occ, matchingHoliday.Name,
                        $"Blocked by {matchingHoliday.Name}"
                    )
                    );
                } else {
                    kept.Add( occ );
                }
            } else /* Allowlist */ {
                if (matchingHoliday is not null) {
                    kept.Add( occ );
                } else {
                    suppressed.Add( new SuppressedOccurrence( occ, string.Empty,
                        "Not on an allowed holiday"
                    )
                    );
                }
            }
        }

        return new ScheduleOccurrenceResult(
            kept.AsReadOnly( ),
            suppressed.AsReadOnly( )
        );
    }

    /// <summary>Default working days: Monday through Friday.</summary>
    private const DaysOfWeek DefaultWorkingDays = DaysOfWeek.Monday | DaysOfWeek.Tuesday
        | DaysOfWeek.Wednesday | DaysOfWeek.Thursday | DaysOfWeek.Friday;

    /// <summary>Maximum number of days to walk when searching for a business day (prevents infinite loops).</summary>
    private const int MaxWalkDays = 30;

    /// <summary>
    /// Holiday-aware overload with shift mode support. Computes raw occurrences via existing recurrence
    /// algorithms, then applies holiday filtering with optional shifting per the specified <paramref name="shiftMode"/>.
    /// </summary>
    /// <param name="schedule">The schedule composite model.</param>
    /// <param name="endOfWindow">End of the preview window (UTC).</param>
    /// <param name="holidayDates">Pre-materialized holiday dates to filter against, or null for no filtering.</param>
    /// <param name="mode">Blocklist (suppress matches) or Allowlist (keep only matches), or null for no filtering.</param>
    /// <param name="shiftMode">How to handle occurrences that fall on non-business days.</param>
    /// <param name="workingDays">Bitmask of days considered working days.</param>
    /// <returns>A <see cref="ScheduleOccurrenceResult"/> with both kept and suppressed/shifted occurrences.</returns>
    public static ScheduleOccurrenceResult CalculateOccurrences(
        Schedule schedule,
        DateTime endOfWindow,
        IReadOnlyList<HolidayDate>? holidayDates,
        HolidayCalendarMode? mode,
        ShiftMode shiftMode,
        DaysOfWeek workingDays = DefaultWorkingDays
    ) {
        // Delegate to the existing 3-arg overload when no shifting is needed
        if (shiftMode == ShiftMode.None) {
            return CalculateOccurrences( schedule, endOfWindow, holidayDates, mode );
        }

        IReadOnlyList<DateTime> rawOccurrences = CalculateOccurrences( schedule, endOfWindow );

        if (holidayDates is null || !holidayDates.Any( ) || mode is null) {
            return new ScheduleOccurrenceResult( rawOccurrences, [] );
        }

        List<DateTime> kept = [];
        List<SuppressedOccurrence> suppressed = [];

        foreach (DateTime occ in rawOccurrences) {
            HolidayDate? matchingHoliday = holidayDates.FirstOrDefault( h => IsOccurrenceOnHoliday( occ, h ) );

            if (mode == HolidayCalendarMode.Blocklist) {
                if (matchingHoliday is not null) {
                    // Occurrence falls on a holiday — shift it
                    DateTime? shifted = FindShiftedDate( occ, shiftMode, workingDays,
                        holidayDates, matchingHoliday );
                    if (shifted.HasValue) {
                        kept.Add( shifted.Value );
                        suppressed.Add( new SuppressedOccurrence( occ, matchingHoliday.Name,
                            $"Shifted from {matchingHoliday.Name} to {shifted.Value:O}",
                            shifted.Value, "Shifted" ) );
                    } else {
                        // Walk guard hit — suppress instead
                        suppressed.Add( new SuppressedOccurrence( occ, matchingHoliday.Name,
                            $"Blocked by {matchingHoliday.Name} (no business day within {MaxWalkDays} days)" ) );
                    }
                } else {
                    kept.Add( occ );
                }
            } else /* Allowlist */ {
                if (matchingHoliday is not null) {
                    kept.Add( occ );
                } else {
                    suppressed.Add( new SuppressedOccurrence( occ, string.Empty,
                        "Not on an allowed holiday" ) );
                }
            }
        }

        return new ScheduleOccurrenceResult( kept.AsReadOnly( ), suppressed.AsReadOnly( ) );
    }

    /// <summary>
    /// Finds the shifted date for an occurrence that falls on a non-business day,
    /// using the specified <paramref name="shiftMode"/> strategy.
    /// Returns null if no business day is found within <see cref="MaxWalkDays"/>.
    /// </summary>
    private static DateTime? FindShiftedDate(
        DateTime occurrence,
        ShiftMode shiftMode,
        DaysOfWeek workingDays,
        IReadOnlyList<HolidayDate> holidays,
        HolidayDate matchingHoliday
    ) {
        return shiftMode switch {
            ShiftMode.NextBusinessDay => WalkToBusinessDay( occurrence, 1, workingDays, holidays ),
            ShiftMode.PreviousBusinessDay => WalkToBusinessDay( occurrence, -1, workingDays, holidays ),
            ShiftMode.NearestBusinessDay => FindNearestBusinessDay( occurrence, workingDays,
                holidays, matchingHoliday ),
            _ => null,
        };
    }

    /// <summary>
    /// Walks forward or backward from <paramref name="origin"/> by <paramref name="direction"/>
    /// (1 = forward, -1 = backward) until a working day that is not a holiday is found.
    /// Returns null if <see cref="MaxWalkDays"/> is exceeded.
    /// </summary>
    private static DateTime? WalkToBusinessDay(
        DateTime origin,
        int direction,
        DaysOfWeek workingDays,
        IReadOnlyList<HolidayDate> holidays
    ) {
        for (int i = 1; i <= MaxWalkDays; i++) {
            DateTime candidate = origin.AddDays( i * direction );
            if (IsWorkingDay( DateOnly.FromDateTime( candidate ), workingDays, holidays )) {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the nearest business day to <paramref name="origin"/>. When equidistant,
    /// uses the holiday's <see cref="ObservanceRule"/> as tiebreaker.
    /// </summary>
    private static DateTime? FindNearestBusinessDay(
        DateTime origin,
        DaysOfWeek workingDays,
        IReadOnlyList<HolidayDate> holidays,
        HolidayDate matchingHoliday
    ) {
        DateTime? next = WalkToBusinessDay( origin, 1, workingDays, holidays );
        DateTime? prev = WalkToBusinessDay( origin, -1, workingDays, holidays );

        if (next is null && prev is null) {
            return null;
        }
        if (next is null) {
            return prev;
        }
        if (prev is null) {
            return next;
        }

        double distNext = ( next.Value - origin ).TotalDays;
        double distPrev = ( origin - prev.Value ).TotalDays;

        if (distNext < distPrev) {
            return next;
        }
        if (distPrev < distNext) {
            return prev;
        }

        // Equidistant: use observance rule as tiebreaker
        // Look up the ObservanceRule from the matching holiday's generating rule
        ObservanceRule obsRule = ObservanceRule.None;
        if (matchingHoliday.GeneratedByRule is not null) {
            obsRule = matchingHoliday.GeneratedByRule.ObservanceRule;
        }

        // SaturdayToMonday favors forward; others (Friday-preference) favor backward
        return obsRule == ObservanceRule.SaturdayToMonday ? next : prev;
    }

    /// <summary>
    /// Checks whether a given date is a working day: it must fall on one of the
    /// <paramref name="workingDays"/> and must not be a holiday.
    /// </summary>
    internal static bool IsWorkingDay(
        DateOnly date,
        DaysOfWeek workingDays,
        IReadOnlyList<HolidayDate> holidays
    ) {
        // Check working day bitmask
        DaysOfWeek dayFlag = date.DayOfWeek switch {
            DayOfWeek.Monday => DaysOfWeek.Monday,
            DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
            DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
            DayOfWeek.Thursday => DaysOfWeek.Thursday,
            DayOfWeek.Friday => DaysOfWeek.Friday,
            DayOfWeek.Saturday => DaysOfWeek.Saturday,
            DayOfWeek.Sunday => DaysOfWeek.Sunday,
            _ => DaysOfWeek.None,
        };

        if ((workingDays & dayFlag) == DaysOfWeek.None) {
            return false;
        }

        // Check if it's a holiday (full-day check only for walk purposes)
        return !holidays.Any( h => h.Date == date );
    }

    /// <summary>
    /// Checks whether a UTC occurrence falls on a holiday date, respecting optional time windows.
    /// </summary>
    internal static bool IsOccurrenceOnHoliday(
        DateTime utcOccurrence,
        HolidayDate holiday
    ) {
        if (holiday.WindowStart is null || holiday.WindowEnd is null ||
            string.IsNullOrEmpty( holiday.WindowTimeZoneId )) {
            // Full-day holiday: compare DateOnly
            DateOnly occDate = DateOnly.FromDateTime( utcOccurrence );
            return occDate == holiday.Date;
        }

        // Time-window holiday: convert UTC occurrence to holiday's timezone
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById( holiday.WindowTimeZoneId );
        DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(
            utcOccurrence,
            tz
        );
        DateOnly localDate = DateOnly.FromDateTime( localTime );

        if (localDate != holiday.Date) {
            return false;
        }

        TimeOnly localTimeOfDay = TimeOnly.FromDateTime( localTime );
        return localTimeOfDay >= holiday.WindowStart.Value && localTimeOfDay <= holiday.WindowEnd.Value;
    }


    #region Repeat, Expiration, and Flow Control

    /// <summary>
    /// This method is used for three purposes:
    /// <list type="number">
    /// <item><description>Checking the <paramref name="expiration"/> UtcTime and exiting early if the
    /// <paramref name="occurrence"/>
    /// is past the Expiration time. (returns true)</description></item>
    /// <item><description>Adding the non-expired <paramref name="occurrence"/> to the <paramref name="result"/>
    /// list.</description></item>
    /// <item><description>Calculating any repeat occurrences and repeating the loop if there are repeat
    /// occurrences.</description></item>
    /// </list>
    /// Under normal operation this method will return false to indicate that the caller should continue processing.
    /// </summary>
    /// <returns>Returns true if <paramref name="occurrence"/> has reached its <paramref name="expiration"/></returns>
    internal static bool AddToResultAndCalculateRepeatOccurrences(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result
    ) {
        long maxRepeat = 0;
        if (repeatOptions != null) {
            maxRepeat = (long)Math.Floor(
                (decimal)((repeatOptions.RepeatDurationMinutes < 0)
                    ? (endOfWindow.AddYears( 1 ) - startDt.UtcTime).TotalMinutes
                    : repeatOptions.RepeatDurationMinutes
                ) / repeatOptions.RepeatIntervalMinutes
            );
        }

        long count = 0;
        do {
            // Check whether the occurrence is past the Expiration time.
            // Exit early and return true so that the caller can return the result list and stop processing.
            if (IsExpired(
                expiration,
                occurrence,
                endOfWindow
            )) { return true; }

            // Add the non-expired occurrence to the result list.
            _ = result.Add( occurrence );

            if (maxRepeat > 0) {
                // Calculate any repeat occurrences.
                occurrence = CalculateRepeatOccurrences(
                    occurrence,
                    repeatOptions!
                );
            }
            count++;
        } while (count <= maxRepeat);

        return false;
    }

    /// <summary>
    /// Calculates the next occurrence of a repeated schedule by adding the repeat interval.
    /// </summary>
    internal static DateTime CalculateRepeatOccurrences(
        DateTime occurrence,
        ScheduleRepeatOptions repeatOptions
    ) => occurrence.AddMinutes( repeatOptions.RepeatIntervalMinutes );

    internal static TimeSpan GetWindowTimeSpan(
        DateTime startTime,
        DateTime endOfWindow,
        ExpirationDateTimeInfo? expiration
    ) {
        if (startTime.Kind != DateTimeKind.Utc) {
            throw new InvalidOperationException( "The startTime DateTime must be in UTC." );
        } else if (endOfWindow.Kind != DateTimeKind.Utc) {
            throw new InvalidOperationException( "The endOfWindow DateTime must be in UTC." );
        }

        DateTime endOfPeriod = expiration != null && expiration.UtcTime < endOfWindow
            ? expiration.UtcTime
            : endOfWindow;
        return endOfPeriod - startTime;
    }

    internal static bool IsExpired(
        ExpirationDateTimeInfo? expiration,
        DateTime occurrence,
        DateTime endOfWindow
    ) =>
        (expiration != null && occurrence >= expiration?.UtcTime) || occurrence >= endOfWindow;

    #endregion Repeat, Expiration, and Flow Control


    #region Daily Occurrences

    /// <summary>
    /// Calculates the daily recurrences until the end of the window or the expiration has passed.
    /// Each instance is calculated by adding the DayInterval to the occurrence date.
    /// </summary>
    internal static void CalculateDailyOccurrences(
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        DailyRecurrence dailyRecurrence
    ) {
        if (dailyRecurrence.DayInterval <= 0) { return; }
        DateTime translatedTime = startDt.TzTime;
        TimeSpan windowPeriod = GetWindowTimeSpan(
            startDt.UtcTime,
            endOfWindow,
            expiration
        );

        for (int i = 0; i < windowPeriod.Days; i++) {
            translatedTime = translatedTime.AddDays( dailyRecurrence.DayInterval );

            // Add the occurrence to the result list and calculate any repeat occurrences.
            // Exit early if the occurrence is past the Expiration time.
            if (AddToResultAndCalculateRepeatOccurrences(
                startDt.ConvertToUtc( translatedTime ),
                startDt,
                repeatOptions,
                expiration,
                endOfWindow,
                result
            )) { return; }
        }
    }

    #endregion Daily Occurrences


    #region Weekly Occurrences

    /// <summary>
    /// Calculates the weekly recurrences until the end of the window or the expiration has passed.
    /// First calculates the remaining occurrences in the first scheduled week, then the remaining window.
    /// </summary>
    internal static void CalculateWeeklyOccurrences(
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        WeeklyRecurrence weeklyRecurrence
    ) {
        if (weeklyRecurrence.WeekInterval <= 0) { return; }
        TimeSpan windowPeriod = GetWindowTimeSpan(
            startDt.UtcTime,
            endOfWindow,
            expiration
        );
        List<DayOfWeek> recurrenceDays = weeklyRecurrence.DaysOfWeek.GetDaysOfWeek( );
        LoopingList<DayOfWeek> loopingWeek = [.. CalendarEnumExtensions.GetWeekOfDays( )];

        DateTime translatedTime = startDt.TzTime;

        int firstWeekEnds = CalculateWeeklyOccurrences_GetDayDifference(
            loopingWeek,
            translatedTime.DayOfWeek,
            loopingWeek[0]
        );

        int weekNum = 0;
        int weekDayCount = 0;
        for (int i = 0; i < windowPeriod.Days; i++) {
            translatedTime = translatedTime.AddDays( 1 );
            DateTime utcTime = startDt.ConvertToUtc( translatedTime );

            if (IsExpired(
                expiration,
                utcTime,
                endOfWindow
            )) { return; }

            if (recurrenceDays.Contains( translatedTime.DayOfWeek ) && (weekNum % weeklyRecurrence.WeekInterval == 0)) {
                if (AddToResultAndCalculateRepeatOccurrences(
                    utcTime,
                    startDt,
                    repeatOptions,
                    expiration,
                    endOfWindow,
                    result
                )) { return; }
            }

            if ((weekDayCount == 0 && i == firstWeekEnds) || (weekDayCount > 0 && weekDayCount % 7 == 0)) {
                weekDayCount++;
                weekNum++;
            } else if (weekDayCount > 0) {
                weekDayCount++;
            }
        }
    }

    /// <summary>
    /// Calculates the number of days between the <paramref name="targetDay"/> and the <paramref name="startDay"/>
    /// by enumerating the <paramref name="loopingWeek"/> list.
    /// </summary>
    internal static int CalculateWeeklyOccurrences_GetDayDifference(
        LoopingList<DayOfWeek> loopingWeek,
        DayOfWeek targetDay,
        DayOfWeek startDay
    ) {
        int count = 0;
        if (targetDay == startDay) { return count; }
        loopingWeek.CurrentIndex = loopingWeek.IndexOf( startDay );
        foreach (DayOfWeek dayOfWeek in loopingWeek) {
            count++;
            if (dayOfWeek == targetDay) { break; }
        }
        return count;
    }

    #endregion Weekly Occurrences


    #region Monthly Occurrences

    /// <summary>
    /// Calculates the monthly recurrences until the end of the window or the expiration has passed.
    /// Monthly recurrence schedules can be calculated using one of two ways, depending on which fields are set:
    /// <list type="number">
    /// <item><description>DayNumbers mode: MonthsOfYear + DayNumbers</description></item>
    /// <item><description>WeekAndDay mode: MonthsOfYear + (WeekNumber + DaysOfWeek)</description></item>
    /// </list>
    /// </summary>
    internal static void CalculateMonthlyOccurrences(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        MonthlyRecurrence monthlyRecurrence,
        TimeOnly startTime
    ) {
        int[] recurrenceMonths = monthlyRecurrence.MonthsOfYear.GetIntMonths( );
        if (monthlyRecurrence.DayNumbers != null) {
            CalculateMonthlyOccurrences_DayNumbersWithinMonth(
                occurrence,
                startDt,
                repeatOptions,
                expiration,
                endOfWindow,
                result,
                monthlyRecurrence,
                recurrenceMonths
            );
        } else {
            CalculateMonthlyOccurrences_WeekAndDay(
                occurrence,
                startDt,
                repeatOptions,
                expiration,
                endOfWindow,
                result,
                monthlyRecurrence,
                startTime,
                recurrenceMonths
            );
        }
    }

    #region DayNumberWithinMonth

    /// <summary>
    /// Calculates the DayNumbers monthly recurrences until the end of the window or the expiration has passed.
    /// The formula is essentially (MonthsOfYear + DayNumbers).
    /// </summary>
    internal static void CalculateMonthlyOccurrences_DayNumbersWithinMonth(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        MonthlyRecurrence monthlyRecurrence,
        int[] recurrenceMonths
    ) {
        int occurrenceYear = occurrence.Year;
        int occurrenceMonth = occurrence.Month;
        int[] dayNumbers = monthlyRecurrence.DayNumbers!;
        Array.Sort(
            dayNumbers,
            CompareNumbers
        );

        if (CalculateMonthlyOccurrences_DayNumbersWithinMonth_EndOfFirstMonth(
            startDt,
            repeatOptions,
            expiration,
            endOfWindow,
            result,
            recurrenceMonths,
            dayNumbers,
            occurrence.Day,
            occurrenceYear,
            occurrenceMonth
        )) { return; }

        int[] remainingRecurrenceMonths = recurrenceMonths.GetRemainingMonthsInYear(
            occurrenceMonth,
            true
        );
        if (remainingRecurrenceMonths.Length == 0) {
            remainingRecurrenceMonths = recurrenceMonths;
            occurrenceYear++;
        }

        CalculateMonthlyOccurrences_DayNumbersWithinMonth_RemainingWindow(
            occurrence,
            startDt,
            repeatOptions,
            expiration,
            endOfWindow,
            result,
            recurrenceMonths,
            remainingRecurrenceMonths,
            dayNumbers,
            occurrenceYear
        );
    }

    /// <summary>
    /// Calculates the remaining DayNumbers monthly recurrences in the first scheduled month.
    /// </summary>
    /// <returns>Returns true if occurrence has reached its expiration or the endOfWindow.</returns>
    internal static bool CalculateMonthlyOccurrences_DayNumbersWithinMonth_EndOfFirstMonth(
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        int[] recurrenceMonths,
        int[] dayNumbers,
        int occurrenceDay,
        int occurrenceYear,
        int occurrenceMonth
    ) {
        if (recurrenceMonths.Contains( occurrenceMonth )) {
            // Finish out the first month
            foreach (int day in CalculateMonthlyOccurrencesDayNumbersWithinMonth(
                occurrenceYear,
                occurrenceMonth,
                dayNumbers
            )) {
                if (day <= occurrenceDay) { continue; }
                DateTime occurrence = startDt.ConvertToUtc(
                    new DateTime(
                        year: occurrenceYear,
                        month: occurrenceMonth,
                        day: day,
                        hour: startDt.TzTime.Hour,
                        minute: startDt.TzTime.Minute,
                        second: startDt.TzTime.Second,
                        kind: DateTimeKind.Unspecified
                    )
                );
                // Add the occurrence to the result list and calculate any repeat occurrences.
                // Exit early if the occurrence is past the Expiration time.
                if (AddToResultAndCalculateRepeatOccurrences(
                    occurrence,
                    startDt,
                    repeatOptions,
                    expiration,
                    endOfWindow,
                    result
                )) { return true; }
            }
        }
        return false;
    }

    /// <summary>
    /// Calculates the monthly recurrences from after the first complete month until the end of the window or
    /// expiration.
    /// </summary>
    internal static void CalculateMonthlyOccurrences_DayNumbersWithinMonth_RemainingWindow(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        int[] recurrenceMonths,
        int[] remainingRecurrenceMonths,
        int[] dayNumbers,
        int occurrenceYear
    ) {
        do {
            foreach (int month in remainingRecurrenceMonths) {
                foreach (int day in CalculateMonthlyOccurrencesDayNumbersWithinMonth(
                    occurrenceYear,
                    month,
                    dayNumbers
                )) {
                    occurrence = startDt.ConvertToUtc(
                        new DateTime(
                            year: occurrenceYear,
                            month: month,
                            day: day,
                            hour: startDt.TzTime.Hour,
                            minute: startDt.TzTime.Minute,
                            second: startDt.TzTime.Second,
                            kind: DateTimeKind.Unspecified
                        )
                    );
                    // Add the occurrence to the result list and calculate any repeat occurrences.
                    // Exit early if the occurrence is past the Expiration time.
                    if (AddToResultAndCalculateRepeatOccurrences(
                        occurrence,
                        startDt,
                        repeatOptions,
                        expiration,
                        endOfWindow,
                        result
                    )) { return; }
                }
            }
            occurrenceYear++;
            remainingRecurrenceMonths = recurrenceMonths;
        } while (occurrence < endOfWindow);
    }

    internal static int[] CalculateMonthlyOccurrencesDayNumbersWithinMonth(
        int year,
        int month,
        int[] dayNums
    ) {
        List<int> result = [];
        foreach (int dayNum in dayNums) {
            result.Add( CalculateMonthlyOccurrences_DayNumbersWithinMonth(
                year,
                month,
                dayNum
            ) );
        }
        return [.. result
            .Distinct( )
            .Where( i => i > 0 )
            .Order( )];
    }

    /// <summary>
    /// Converts negative day numbers into their positive counterparts.
    /// </summary>
    /// <returns>The positive integer corresponding to the negative day of month, or 0 if the date does not correspond
    /// to a day within the month.</returns>
    internal static int CalculateMonthlyOccurrences_DayNumbersWithinMonth(
        int year,
        int month,
        int day
    ) {
        int daysInMonth = DateTime.DaysInMonth(
            year,
            month
        );
        return day >= 0
            ? day <= daysInMonth
                ? day
                : 0
            : (day * -1) <= daysInMonth
                ? daysInMonth + day + 1
                : 0;
    }

    internal static int CompareNumbers(
        int x,
        int y
    ) =>
        x > 0 && y > 0  // Both numbers are positive
            ? x.CompareTo( y )
            : x > 0 && y < 0 // x is positive, y is negative
                ? -1
                : x < 0 && y > 0  // x is negative, y is positive
                    ? 1
                    : -x.CompareTo( -y ); // Both numbers are negative

    #endregion DayNumberWithinMonth


    #region WeekAndDay

    /// <summary>
    /// Calculates the WeekAndDay monthly recurrences until the end of the window or the expiration has passed.
    /// The formula is essentially (MonthsOfYear + (WeekNumber + DaysOfWeek)).
    /// </summary>
    internal static void CalculateMonthlyOccurrences_WeekAndDay(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        MonthlyRecurrence monthlyRecurrence,
        TimeOnly startTime,
        int[] recurrenceMonths
    ) {
        int occurrenceYear = occurrence.Year;
        int occurrenceMonth = occurrence.Month;
        List<DayOfWeek> daysOfWeek = ( (DaysOfWeek) monthlyRecurrence.DaysOfWeek! ).GetDaysOfWeek( ).OrderDaysOfWeek( );
        List<DayOfWeek> weekOfDays = CalendarEnumExtensions.GetWeekOfDays( );
        int[] weekNumbers = monthlyRecurrence.WeekNumber!.Value.GetWeekNumbersInMonth( );

        if (CalculateMonthlyOccurrences_WeekAndDay_EndOfFirstMonth(
            occurrence,
            startDt,
            repeatOptions,
            expiration,
            endOfWindow,
            result,
            startTime,
            recurrenceMonths,
            daysOfWeek,
            weekOfDays,
            weekNumbers,
            occurrenceYear,
            occurrenceMonth
        )) { return; }

        int[] remainingRecurrenceMonths = recurrenceMonths.GetRemainingMonthsInYear(
            occurrenceMonth,
            true
        );
        if (remainingRecurrenceMonths.Length == 0) {
            remainingRecurrenceMonths = recurrenceMonths;
            occurrenceYear++;
        }

        CalculateMonthlyOccurrences_WeekAndDay_RemainingWindow(
            occurrence,
            startDt,
            repeatOptions,
            expiration,
            endOfWindow,
            result,
            startTime,
            remainingRecurrenceMonths,
            recurrenceMonths,
            occurrenceYear,
            daysOfWeek,
            weekOfDays,
            weekNumbers
        );
    }

    /// <summary>
    /// Finishes out the first month for WeekAndDay recurrence.
    /// </summary>
    /// <returns>Returns true if occurrence has reached its expiration or the endOfWindow.</returns>
    internal static bool CalculateMonthlyOccurrences_WeekAndDay_EndOfFirstMonth(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        TimeOnly startTime,
        int[] recurrenceMonths,
        List<DayOfWeek> daysOfWeek,
        List<DayOfWeek> weekOfDays,
        int[] weekNumbers,
        int occurrenceYear,
        int occurrenceMonth
    ) {
        if (recurrenceMonths.Contains( occurrenceMonth )) {
            int daysInMonth = DateTime.DaysInMonth(
                occurrenceYear,
                occurrenceMonth
            );
            DateTime occurrenceMonthStart = new(
                occurrenceYear,
                occurrenceMonth,
                1
            );
            List<DayOfWeek> daysInFirstWeek = occurrenceMonthStart.DayOfWeek.GetDaysInWeekFromStartDay( );
            List<DayOfWeek> daysInLastWeek = CalculateMonthlyOccurrences_WeekAndDay_GetDaysInLastWeekOfMonth(
                weekOfDays,
                daysInMonth,
                daysInFirstWeek.Count
            );
            List<DayOfWeek> remainingDaysOfWeek = daysOfWeek.GetRemainingDaysInWeek(
                occurrenceMonthStart.DayOfWeek,
                true
            );

            int weekCount = CalculateMonthlyOccurrences_WeekAndDay_GetWeekCount(
                daysInMonth,
                daysInFirstWeek.Count,
                daysInLastWeek.Count
            );

            int currentWeek = Convert.ToInt32( Math.Floor( (decimal) occurrence.Day / 7 ) );
            int[] remainingWeekNumbers = [
                .. weekNumbers.Where(
                    weekNum => ( weekNum >= currentWeek ) && ( weekNum <= weekCount )
                )
            ];

            if (CalculateMonthlyOccurrences_WeekAndDay_RemainingMonth(
                occurrence,
                startDt,
                repeatOptions,
                expiration,
                endOfWindow,
                result,
                startTime,
                occurrenceYear,
                occurrenceMonth,
                weekNumbers,
                weekCount,
                daysOfWeek,
                remainingDaysOfWeek,
                daysInFirstWeek,
                daysInLastWeek
            )) { return true; }
        }
        return false;
    }

    /// <summary>
    /// Calculates the monthly recurrences from after the first complete month until the end of the window or
    /// expiration.
    /// </summary>
    internal static void CalculateMonthlyOccurrences_WeekAndDay_RemainingWindow(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        TimeOnly startTime,
        int[] remainingRecurrenceMonths,
        int[] recurrenceMonths,
        int occurrenceYear,
        List<DayOfWeek> daysOfWeek,
        List<DayOfWeek> weekOfDays,
        int[] weekNumbers
    ) {
        do {
            foreach (int month in remainingRecurrenceMonths) {
                int daysInMonth = DateTime.DaysInMonth(
                    occurrenceYear,
                    month
                );
                DateTime occurrenceMonthStart = new(
                    occurrenceYear,
                    month,
                    1
                );
                List<DayOfWeek> daysInFirstWeek = occurrenceMonthStart.DayOfWeek.GetDaysInWeekFromStartDay( );
                List<DayOfWeek> daysInLastWeek = CalculateMonthlyOccurrences_WeekAndDay_GetDaysInLastWeekOfMonth(
                    weekOfDays,
                    daysInMonth,
                    daysInFirstWeek.Count
                );
                int weekCount = CalculateMonthlyOccurrences_WeekAndDay_GetWeekCount(
                    daysInMonth,
                    daysInFirstWeek.Count,
                    daysInLastWeek.Count
                );
                if (CalculateMonthlyOccurrences_WeekAndDay_RemainingMonth(
                    occurrence,
                    startDt,
                    repeatOptions,
                    expiration,
                    endOfWindow,
                    result,
                    startTime,
                    occurrenceYear,
                    month,
                    weekNumbers,
                    weekCount,
                    daysOfWeek,
                    daysOfWeek,
                    daysInFirstWeek,
                    daysInLastWeek
                )) { return; }
            }
            occurrenceYear++;
            remainingRecurrenceMonths = recurrenceMonths;
        } while (occurrence < endOfWindow);
    }

    internal static List<DayOfWeek> CalculateMonthlyOccurrences_WeekAndDay_GetDaysInLastWeekOfMonth(
        List<DayOfWeek> weekOfDays,
        int daysInMonth,
        int daysInFirstWeek
    ) {
        int numDaysInLastWeek = ( daysInMonth - daysInFirstWeek ) % 7;
        List<DayOfWeek> daysInLastWeek = [];
        int count = 0;
        foreach (DayOfWeek dayOfWeek in weekOfDays) {
            count++;
            daysInLastWeek.Add( dayOfWeek );
            if (count == numDaysInLastWeek) { break; }
        }
        return daysInLastWeek;
    }

    internal static int CalculateMonthlyOccurrences_WeekAndDay_GetWeekCount(
        int daysInMonth,
        int daysInFirstWeek,
        int daysInLastWeek
    ) => ((daysInMonth - daysInFirstWeek - daysInLastWeek) / 7) + 2;

    /// <summary>
    /// The primary Monthly Recurrence WeekAndDay logic.
    /// </summary>
    /// <returns>Returns true if occurrence has reached its expiration or the endOfWindow.</returns>
    internal static bool CalculateMonthlyOccurrences_WeekAndDay_RemainingMonth(
        DateTime occurrence,
        StartDateTimeInfo startDt,
        ScheduleRepeatOptions? repeatOptions,
        ExpirationDateTimeInfo? expiration,
        DateTime endOfWindow,
        HashSet<DateTime> result,
        TimeOnly startTime,
        int occurrenceYear,
        int occurrenceMonth,
        int[] weekNumbers,
        int weekCount,
        List<DayOfWeek> daysOfWeek,
        List<DayOfWeek> remainingDaysOfWeek,
        List<DayOfWeek> daysInFirstWeek,
        List<DayOfWeek> daysInLastWeek
    ) {
        foreach (int week in weekNumbers.Where( week => week <= weekCount )) {
            foreach (DayOfWeek day in remainingDaysOfWeek) {
                int dayNum = 0;
                if (week == 1) {
                    if (daysInFirstWeek.Contains( day )) {
                        dayNum = daysInFirstWeek.IndexOf( day ) + 1;
                    } else {
                        continue;
                    }
                } else if (week == weekCount) {
                    if (daysInLastWeek.Contains( day )) {
                        dayNum = ((week - 1) * 7) + daysInLastWeek.IndexOf( day ) + 1;
                    } else {
                        continue;
                    }
                } else {
                    dayNum = ((week - 1) * 7) + daysOfWeek.IndexOf( day ) + 1;
                }

                // Guard against invalid day numbers (formula can exceed days-in-month for partial first/last weeks).
                int maxDay = DateTime.DaysInMonth(
                    occurrenceYear,
                    occurrenceMonth
                );
                if (dayNum < 1 || dayNum > maxDay) { continue; }

                occurrence = startDt.ConvertToUtc(
                    new DateTime(
                        year: occurrenceYear,
                        month: occurrenceMonth,
                        day: dayNum,
                        hour: startDt.TzTime.Hour,
                        minute: startDt.TzTime.Minute,
                        second: startDt.TzTime.Second,
                        kind: DateTimeKind.Unspecified
                    )
                );

                // Add the occurrence to the result list and calculate any repeat occurrences.
                // Exit early if the occurrence is past the Expiration time.
                if (AddToResultAndCalculateRepeatOccurrences(
                    occurrence,
                    startDt,
                    repeatOptions,
                    expiration,
                    endOfWindow,
                    result
                )) { return true; }
            }
            remainingDaysOfWeek = daysOfWeek;
        }

        return false;
    }

    #endregion WeekAndDay

    #endregion Monthly Occurrences

    #endregion Calculate Occurrences
}
