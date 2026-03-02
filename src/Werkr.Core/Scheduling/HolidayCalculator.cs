using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Static computation engine that evaluates <see cref="HolidayRule"/> definitions
/// into concrete <see cref="HolidayDate"/> instances for given year(s).
/// </summary>
public static class HolidayCalculator {
    /// <summary>
    /// Evaluates a single rule for a specific year and returns any resulting holiday dates.
    /// Returns an empty list if the year is outside the rule's <c>YearStart</c>/<c>YearEnd</c> bounds.
    /// </summary>
    public static IReadOnlyList<HolidayDate> ComputeDatesForYear( HolidayRule rule, int year ) {
        // Year bounds check
        if (rule.YearStart.HasValue && year < rule.YearStart.Value) {
            return [];
        }

        if (rule.YearEnd.HasValue && year > rule.YearEnd.Value) {
            return [];
        }

        DateOnly? rawDate = rule.RuleType switch {
            HolidayRuleType.FixedDate => ComputeFixedDate( rule, year ),
            HolidayRuleType.NthWeekdayOfMonth => ComputeNthWeekday( rule, year ),
            HolidayRuleType.LastWeekdayOfMonth => ComputeLastWeekday( rule, year ),
            _ => null,
        };

        if (rawDate is null) {
            return [];
        }

        // Apply observance rule (weekend shifting) for FixedDate rules
        DateOnly observed = rule.RuleType == HolidayRuleType.FixedDate
            ? ApplyObservanceRule( rawDate.Value, rule.ObservanceRule )
            : rawDate.Value;

        return [
            new HolidayDate {
                HolidayCalendarId = rule.HolidayCalendarId,
                HolidayRuleId = rule.Id,
                Date = observed,
                Name = rule.Name,
                Year = year,
                WindowStart = rule.WindowStart,
                WindowEnd = rule.WindowEnd,
                WindowTimeZoneId = rule.WindowTimeZoneId,
            }
        ];
    }

    /// <summary>
    /// Evaluates all rules in a calendar for a specific year.
    /// </summary>
    public static IReadOnlyList<HolidayDate> ComputeAllDatesForYear( HolidayCalendar calendar, int year ) {
        List<HolidayDate> results = [];
        foreach (HolidayRule rule in calendar.Rules) {
            results.AddRange( ComputeDatesForYear( rule, year ) );
        }
        return results;
    }

    /// <summary>
    /// Batch computation across a year range (inclusive).
    /// </summary>
    public static IReadOnlyList<HolidayDate> ComputeDatesForRange( HolidayCalendar calendar, int startYear, int endYear ) {
        List<HolidayDate> results = [];
        for (int year = startYear; year <= endYear; year++) {
            results.AddRange( ComputeAllDatesForYear( calendar, year ) );
        }
        return results;
    }

    /// <summary>
    /// Applies the observance rule to shift a holiday that falls on a weekend.
    /// </summary>
    internal static DateOnly ApplyObservanceRule( DateOnly date, ObservanceRule rule ) => rule switch {
        ObservanceRule.None => date,
        ObservanceRule.SaturdayToFriday_SundayToMonday => date.DayOfWeek switch {
            DayOfWeek.Saturday => date.AddDays( -1 ),
            DayOfWeek.Sunday => date.AddDays( 1 ),
            _ => date,
        },
        ObservanceRule.SaturdayToMonday => date.DayOfWeek switch {
            DayOfWeek.Saturday => date.AddDays( 2 ),
            _ => date,
        },
        ObservanceRule.NearestWeekday => date.DayOfWeek switch {
            DayOfWeek.Saturday => date.AddDays( -1 ),
            DayOfWeek.Sunday => date.AddDays( 1 ),
            _ => date,
        },
        _ => date,
    };

    /// <summary>
    /// Returns the Nth occurrence of a weekday in a given month, or null if there aren't enough.
    /// </summary>
    internal static DateOnly? GetNthWeekdayOfMonth( int year, int month, DayOfWeek day, int n ) {
        DateOnly first = new( year, month, 1 );
        int daysUntilTarget = ((int) day - (int) first.DayOfWeek + 7) % 7;
        DateOnly firstOccurrence = first.AddDays( daysUntilTarget );
        DateOnly nthOccurrence = firstOccurrence.AddDays( 7 * (n - 1) );

        // Verify we're still in the same month
        return nthOccurrence.Month == month ? nthOccurrence : null;
    }

    /// <summary>
    /// Returns the last occurrence of a weekday in a given month.
    /// </summary>
    internal static DateOnly GetLastWeekdayOfMonth( int year, int month, DayOfWeek day ) {
        int daysInMonth = DateTime.DaysInMonth( year, month );
        DateOnly last = new( year, month, daysInMonth );
        int daysBack = ((int) last.DayOfWeek - (int) day + 7) % 7;
        return last.AddDays( -daysBack );
    }

    /// <summary>Fixed date with observance shift.</summary>
    private static DateOnly? ComputeFixedDate( HolidayRule rule, int year ) {
        if (!rule.Month.HasValue || !rule.Day.HasValue) {
            return null;
        }

        int month = rule.Month.Value;
        int day = rule.Day.Value;

        // Handle Feb 29 in non-leap years
        if (month == 2 && day == 29 && !DateTime.IsLeapYear( year )) {
            return null;
        }

        // Validate day for the month
        return day > DateTime.DaysInMonth( year, month ) ? null : new DateOnly( year, month, day );
    }

    /// <summary>Nth weekday of month.</summary>
    private static DateOnly? ComputeNthWeekday( HolidayRule rule, int year ) {
        return !rule.Month.HasValue || !rule.DayOfWeek.HasValue || !rule.WeekNumber.HasValue
            ? null
            : GetNthWeekdayOfMonth( year, rule.Month.Value, rule.DayOfWeek.Value, rule.WeekNumber.Value );
    }

    /// <summary>Last weekday of month.</summary>
    private static DateOnly? ComputeLastWeekday( HolidayRule rule, int year ) {
        return !rule.Month.HasValue || !rule.DayOfWeek.HasValue
            ? null
            : GetLastWeekdayOfMonth( year, rule.Month.Value, rule.DayOfWeek.Value );
    }
}
