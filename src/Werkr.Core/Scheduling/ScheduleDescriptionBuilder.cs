using System.Text;

using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Ranges;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Produces human-readable descriptions of a <see cref="Schedule"/> composite.
/// <para>
/// Example outputs:
///   "Once on 2026-03-15 at 9:00 AM EST"
///   "Every 2 days at 9:00 AM EST"
///   "Weekly on Mon, Wed, Fri at 9:00 AM EST"
///   "Monthly on the 1st and 15th in Jan–Mar, Jun at 9:00 AM EST"
/// </para>
/// </summary>
public static class ScheduleDescriptionBuilder {
    /// <summary>
    /// Returns a friendly description string for the given schedule.
    /// </summary>
    public static string GetFriendlyDescription( Schedule schedule ) {
        StringBuilder sb = new( );

        // Recurrence pattern
        if (schedule.DailyRecurrence is not null) {
            AppendDailyDescription( sb, schedule.DailyRecurrence );
        } else if (schedule.WeeklyRecurrence is not null) {
            AppendWeeklyDescription( sb, schedule.WeeklyRecurrence );
        } else if (schedule.MonthlyRecurrence is not null) {
            AppendMonthlyDescription( sb, schedule.MonthlyRecurrence );
        } else {
            _ = sb.Append( "Once" );
            if (schedule.StartDateTime is not null) {
                _ = sb.Append( $" on {schedule.StartDateTime.Date:yyyy-MM-dd}" );
            }
        }

        // Time and timezone
        if (schedule.StartDateTime is not null) {
            string timeStr = schedule.StartDateTime.Time.ToString( "h:mm tt" );
            string tzAbbrev = GetTimeZoneAbbreviation( schedule.StartDateTime.TimeZone );
            _ = sb.Append( $" at {timeStr} {tzAbbrev}" );
        }

        // Repeat options
        if (schedule.RepeatOptions is not null) {
            AppendRepeatDescription( sb, schedule.RepeatOptions );
        }

        // Expiration
        if (schedule.Expiration is not null) {
            string expTimeStr = schedule.Expiration.Time.ToString( "h:mm tt" );
            string expTzAbbrev = GetTimeZoneAbbreviation( schedule.Expiration.TimeZone );
            _ = sb.Append( $" until {schedule.Expiration.Date:yyyy-MM-dd} at {expTimeStr} {expTzAbbrev}" );
        }

        // Holiday calendar
        if (schedule.HolidayCalendar is not null && schedule.HolidayCalendarMode is not null) {
            string calName = schedule.HolidayCalendar.Name;
            _ = schedule.HolidayCalendarMode.Value switch {
                HolidayCalendarMode.Blocklist => sb.Append( $", excluding {calName}" ),
                HolidayCalendarMode.Allowlist => sb.Append( $", only on {calName}" ),
                _ => sb,
            };
        }

        return sb.ToString( );
    }

    private static void AppendDailyDescription( StringBuilder sb, DailyRecurrence daily ) {
        _ = daily.DayInterval == 1 ? sb.Append( "Daily" ) : sb.Append( $"Every {daily.DayInterval} days" );
    }

    private static void AppendWeeklyDescription( StringBuilder sb, WeeklyRecurrence weekly ) {
        string days = RangeOfDays.ToString( RangeOfDays.GetContiguousRanges( weekly.DaysOfWeek ), abbreviated: true );

        _ = weekly.WeekInterval == 1 ? sb.Append( $"Weekly on {days}" ) : sb.Append( $"Every {weekly.WeekInterval} weeks on {days}" );
    }

    private static void AppendMonthlyDescription( StringBuilder sb, MonthlyRecurrence monthly ) {
        string months = RangeOfMonths.ToString(
            RangeOfMonths.GetContiguousRanges( monthly.MonthsOfYear ), abbreviated: true );

        if (monthly.DayNumbers is { Length: > 0 }) {
            // Day-number mode
            string dayList = FormatDayNumbers( monthly.DayNumbers );
            _ = sb.Append( $"Monthly on the {dayList} in {months}" );
        } else if (monthly.WeekNumber is not null && monthly.DaysOfWeek is not null) {
            // Week+day mode
            string weekNums = RangeOfWeekNums.ToString(
                RangeOfWeekNums.GetContiguousRanges( monthly.WeekNumber.Value ) );
            string days = ( (DaysOfWeek) monthly.DaysOfWeek ).ToString( abbreviated: true );
            _ = sb.Append( $"Monthly on the {weekNums} {days} in {months}" );
        } else {
            _ = sb.Append( $"Monthly in {months}" );
        }
    }

    private static void AppendRepeatDescription( StringBuilder sb, ScheduleRepeatOptions options ) {
        string interval = FormatMinutes( options.RepeatIntervalMinutes );

        if (options.RepeatDurationMinutes < 0) {
            _ = sb.Append( $" repeating every {interval} indefinitely" );
        } else {
            string duration = FormatMinutes( options.RepeatDurationMinutes );
            _ = sb.Append( $" repeating every {interval} for {duration}" );
        }
    }

    private static string FormatMinutes( int minutes ) {
        if (minutes >= 1440 && minutes % 1440 == 0) {
            int days = minutes / 1440;
            return days == 1 ? "1 day" : $"{days} days";
        }
        if (minutes >= 60 && minutes % 60 == 0) {
            int hours = minutes / 60;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }
        return minutes == 1 ? "1 min" : $"{minutes} min";
    }

    private static string FormatDayNumbers( int[] dayNumbers ) {
        int[] sorted = [.. dayNumbers.Order( )];
        string[] formatted = new string[sorted.Length];
        for (int i = 0; i < sorted.Length; i++) {
            formatted[i] = GetOrdinal( sorted[i] );
        }
        return formatted.Length switch {
            1 => formatted[0],
            2 => $"{formatted[0]} and {formatted[1]}",
            _ => string.Join( ", ", formatted[..^1] ) + $", and {formatted[^1]}",
        };
    }

    private static string GetOrdinal( int number ) {
        if (number < 0) {
            return $"{number}th-from-end";
        }

        int abs = Math.Abs( number );
        string suffix = ( abs % 100 ) switch {
            11 or 12 or 13 => "th",
            _ => ( abs % 10 ) switch {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            }
        };
        return $"{number}{suffix}";
    }

    private static string GetTimeZoneAbbreviation( TimeZoneInfo tz ) {
        // Use the standard abbreviation (e.g., "EST", "PST", "UTC")
        // TimeZoneInfo doesn't expose abbreviations directly, so derive from StandardName
        if (tz == TimeZoneInfo.Utc) {
            return "UTC";
        }

        string standardName = tz.StandardName;
        // If the standard name is a short abbreviation already, use it
        if (standardName.Length <= 5) {
            return standardName;
        }

        // Otherwise, build an abbreviation from the capital letters
        StringBuilder abbrev = new( );
        foreach (char c in standardName) {
            if (char.IsUpper( c )) {
                _ = abbrev.Append( c );
            }
        }
        string result = abbrev.ToString( );
        return result.Length >= 2 ? result : standardName;
    }
}
