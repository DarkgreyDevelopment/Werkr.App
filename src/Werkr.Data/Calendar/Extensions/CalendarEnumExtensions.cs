using System.Globalization;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Collections;
using Werkr.Data.Ranges;

namespace Werkr.Data.Calendar.Extensions;

/// <summary>
/// Provides extension methods for working with <see cref="DaysOfWeek"/>/<see cref="DayOfWeek"/>,
/// <see cref="MonthsOfYear"/>/<see cref="Month"/>, and <see cref="WeekNumberWithinMonth"/> enums.
/// </summary>
public static class CalendarEnumExtensions {

    #region DaysOfWeek / DayOfWeek

    // TODO: inject via IOptions<CalendarSettings>

    /// <summary>
    /// Gets or sets the day that is considered the start of the week when ordering <see cref="DayOfWeek"/> collections. Defaults to <see cref="DayOfWeek.Monday"/>.
    /// </summary>
    public static DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

    /// <summary>
    /// Converts <see cref="DaysOfWeek"/> flags into their <see cref="DayOfWeek"/> counterparts.
    /// </summary>
    /// <param name="daysOfWeek">The flags enum value.</param>
    /// <returns>A <see cref="List{T}"/> of matching <see cref="DayOfWeek"/> values.</returns>
    public static List<DayOfWeek> GetDaysOfWeek( this DaysOfWeek daysOfWeek ) {
        List<DayOfWeek> result = [];
        if (daysOfWeek.HasFlag( DaysOfWeek.Monday )) { result.Add( DayOfWeek.Monday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Tuesday )) { result.Add( DayOfWeek.Tuesday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Wednesday )) { result.Add( DayOfWeek.Wednesday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Thursday )) { result.Add( DayOfWeek.Thursday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Friday )) { result.Add( DayOfWeek.Friday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Saturday )) { result.Add( DayOfWeek.Saturday ); }
        if (daysOfWeek.HasFlag( DaysOfWeek.Sunday )) { result.Add( DayOfWeek.Sunday ); }
        return result;
    }

    /// <summary>
    /// Returns the next <see cref="DayOfWeek"/> in the list after <paramref name="startDay"/> (exclusive).
    /// Returns <see langword="null"/> if the next day would be the <see cref="WeekStartDay"/>.
    /// </summary>
    public static DayOfWeek? GetNextDayInWeek( this List<DayOfWeek> daysOfWeek, DayOfWeek startDay ) {
        List<DayOfWeek> result = daysOfWeek.GetRemainingDaysInWeek( startDay, true );
        return result.Count > 0 ? result[0] : null;
    }

    /// <summary>
    /// Gets the remaining days in the week from <paramref name="startDay"/> that are
    /// present in <paramref name="daysOfWeek"/>.
    /// </summary>
    /// <param name="daysOfWeek">The candidate days to filter.</param>
    /// <param name="startDay">The reference start day.</param>
    /// <param name="exclusive">If <see langword="true"/>, excludes the start day itself.</param>
    public static List<DayOfWeek> GetRemainingDaysInWeek( this List<DayOfWeek> daysOfWeek, DayOfWeek startDay, bool exclusive = false ) {
        List<DayOfWeek> result = [];
        List<DayOfWeek> remainingWeekDays = startDay.GetDaysInWeekFromStartDay( exclusive );

        foreach (DayOfWeek dayOfWeek in remainingWeekDays) {
            if (daysOfWeek.Contains( dayOfWeek )) {
                result.Add( dayOfWeek );
            }
        }
        return result;
    }

    /// <summary>
    /// Returns an ordered list of days from <paramref name="startDay"/> to the end of the week,
    /// based on the configured <see cref="WeekStartDay"/>.
    /// </summary>
    /// <param name="startDay">The day to start from.</param>
    /// <param name="exclusive">If <see langword="true"/>, excludes the start day itself.</param>
    public static List<DayOfWeek> GetDaysInWeekFromStartDay( this DayOfWeek startDay, bool exclusive = false ) {
        List<DayOfWeek> result = [];
        List<DayOfWeek> weekOfDays = GetWeekOfDays( );
        int startPosition = weekOfDays.IndexOf( startDay );
        if (exclusive) { startPosition++; }
        for (int i = startPosition; i < weekOfDays.Count; i++) {
            result.Add( weekOfDays[i] );
        }
        return result;
    }

    /// <summary>
    /// Returns all 7 <see cref="DayOfWeek"/> values in unordered fashion.
    /// </summary>
    public static List<DayOfWeek> GetUnorderedWeekOfDays( ) =>
        GetDaysOfWeek( (DaysOfWeek)127 );

    /// <summary>
    /// Returns all 7 <see cref="DayOfWeek"/> values ordered from the configured <see cref="WeekStartDay"/>.
    /// </summary>
    public static List<DayOfWeek> GetWeekOfDays( ) =>
        OrderDaysOfWeek( GetUnorderedWeekOfDays( ) );

    /// <summary>
    /// Orders the days starting from the configured <see cref="WeekStartDay"/>.
    /// </summary>
    public static List<DayOfWeek> OrderDaysOfWeek( this List<DayOfWeek> daysOfWeek ) =>
        OrderDaysOfWeek( daysOfWeek, WeekStartDay );

    /// <summary>
    /// Orders the days starting from the specified <paramref name="startDay"/>.
    /// </summary>
    public static List<DayOfWeek> OrderDaysOfWeek( this List<DayOfWeek> daysOfWeek, DayOfWeek startDay ) {
        List<DayOfWeek> result = [];
        LoopingList<DayOfWeek> loopingDays = [.. GetUnorderedWeekOfDays( )];
        loopingDays.CurrentIndex = loopingDays.IndexOf( startDay );
        int count = 1;
        foreach (DayOfWeek dayOfWeek in loopingDays) {
            if (count > 7) { break; }
            if (daysOfWeek.Contains( dayOfWeek )) {
                result.Add( dayOfWeek );
            }
            count++;
        }
        return result;
    }

    /// <summary>
    /// Formats <see cref="DaysOfWeek"/> flags as a human-readable string using contiguous ranges.
    /// </summary>
    /// <param name="daysOfWeek">The flags to format.</param>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses abbreviations (e.g., "Mon").
    /// When <see langword="false"/>, uses full names (e.g., "Monday").
    /// </param>
    public static string ToString( this DaysOfWeek daysOfWeek, bool abbreviated = false )
        => RangeOfDays.ToString( RangeOfDays.GetContiguousRanges( daysOfWeek ), abbreviated );

    /// <summary>
    /// Formats a list of <see cref="DayOfWeek"/> as a human-readable string using contiguous ranges.
    /// </summary>
    public static string ToString( this List<DayOfWeek> daysOfWeek, bool abbreviated = false )
        => RangeOfDays.ToString( RangeOfDays.GetContiguousRanges( daysOfWeek ), abbreviated );

    /// <summary>
    /// Formats a single <see cref="DayOfWeek"/> as a string.
    /// </summary>
    /// <param name="day">The day to format.</param>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, returns the abbreviated name (e.g., "Mon").
    /// When <see langword="false"/>, returns the full name (e.g., "Monday").
    /// </param>
    public static string ToString( this DayOfWeek day, bool abbreviated )
        => abbreviated
            ? DateTimeFormatInfo.CurrentInfo.GetAbbreviatedDayName( day )
            : DateTimeFormatInfo.CurrentInfo.GetDayName( day );

    #endregion DaysOfWeek / DayOfWeek

    #region MonthsOfYear / Month

    /// <summary>
    /// Filters to months after the given <paramref name="startMonth"/>.
    /// </summary>
    /// <param name="months">The candidate month integers (1-12).</param>
    /// <param name="startMonth">The reference month number.</param>
    /// <param name="exclusive">If <see langword="true"/>, excludes the start month itself.</param>
    public static int[] GetRemainingMonthsInYear( this int[] months, int startMonth, bool exclusive = false ) =>
        exclusive
            ? [.. months.Where( month => month > startMonth )]
            : [.. months.Where( month => month >= startMonth )];

    /// <summary>
    /// Converts a list of <see cref="Month"/> values into their integer counterparts (1-12).
    /// </summary>
    public static int[] GetIntMonths( this MonthsOfYear monthsOfYear )
        => [.. GetIntMonths( monthsOfYear.GetMonths( ) )];

    /// <summary>
    /// Converts a list of <see cref="Month"/> values into their integer counterparts (1-12).
    /// </summary>
    public static int[] GetIntMonths( this List<Month> monthsOfYear )
        => [.. monthsOfYear.Select( month => (int)month )];

    /// <summary>
    /// Converts <see cref="MonthsOfYear"/> flags into a list of <see cref="Month"/> values.
    /// </summary>
    public static List<Month> GetMonths( this MonthsOfYear monthsOfYear ) {
        List<Month> result = [];
        if (monthsOfYear.HasFlag( MonthsOfYear.January )) { result.Add( Month.January ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.February )) { result.Add( Month.February ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.March )) { result.Add( Month.March ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.April )) { result.Add( Month.April ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.May )) { result.Add( Month.May ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.June )) { result.Add( Month.June ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.July )) { result.Add( Month.July ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.August )) { result.Add( Month.August ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.September )) { result.Add( Month.September ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.October )) { result.Add( Month.October ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.November )) { result.Add( Month.November ); }
        if (monthsOfYear.HasFlag( MonthsOfYear.December )) { result.Add( Month.December ); }
        return result;
    }

    /// <summary>
    /// Converts a list of <see cref="Month"/> values back to <see cref="MonthsOfYear"/> flags.
    /// </summary>
    public static MonthsOfYear GetMonths( this List<Month> monthsOfYear ) {
        MonthsOfYear result = new( );
        foreach (Month month in monthsOfYear) {
            result |= month switch {
                Month.January => MonthsOfYear.January,
                Month.February => MonthsOfYear.February,
                Month.March => MonthsOfYear.March,
                Month.April => MonthsOfYear.April,
                Month.May => MonthsOfYear.May,
                Month.June => MonthsOfYear.June,
                Month.July => MonthsOfYear.July,
                Month.August => MonthsOfYear.August,
                Month.September => MonthsOfYear.September,
                Month.October => MonthsOfYear.October,
                Month.November => MonthsOfYear.November,
                Month.December => MonthsOfYear.December,
                _ => 0,
            };
        }
        return result;
    }

    /// <summary>
    /// Formats <see cref="MonthsOfYear"/> flags as a human-readable string using contiguous ranges.
    /// </summary>
    /// <param name="monthsOfYear">The flags to format.</param>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses abbreviations (e.g., "Jan").
    /// When <see langword="false"/>, uses full names (e.g., "January").
    /// </param>
    public static string ToString( this MonthsOfYear monthsOfYear, bool abbreviated = false )
        => RangeOfMonths.ToString( RangeOfMonths.GetContiguousRanges( monthsOfYear ), abbreviated );

    /// <summary>
    /// Formats a list of <see cref="Month"/> values as a human-readable string using contiguous ranges.
    /// </summary>
    public static string ToString( this List<Month> monthsOfYear, bool abbreviated = false )
        => RangeOfMonths.ToString( RangeOfMonths.GetContiguousRanges( monthsOfYear ), abbreviated );

    /// <summary>
    /// Formats a single <see cref="Month"/> as a string.
    /// </summary>
    /// <param name="month">The month to format.</param>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, returns the abbreviated name (e.g., "Jan").
    /// When <see langword="false"/>, returns the full name (e.g., "January").
    /// </param>
    public static string ToString( this Month month, bool abbreviated )
        => abbreviated
            ? DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName( (int)month )
            : DateTimeFormatInfo.CurrentInfo.GetMonthName( (int)month );

    /// <summary>
    /// Converts a 1-based month number (1-12) to the corresponding culture-invariant month name string.
    /// </summary>
    /// <param name="month">The month number (1 = January, 12 = December).</param>
    public static string GetMonthNameFromMonthNum( this int month )
        => DateTimeFormatInfo.CurrentInfo.GetMonthName( month );

    #endregion MonthsOfYear / Month

    #region WeekNumberWithinMonth

    /// <summary>
    /// Converts <see cref="WeekNumberWithinMonth"/> flags into an array of 1-based week numbers.
    /// </summary>
    public static int[] GetWeekNumbersInMonth( this WeekNumberWithinMonth weekNums ) {
        List<int> result = [];
        if (weekNums.HasFlag( WeekNumberWithinMonth.First )) { result.Add( 1 ); }
        if (weekNums.HasFlag( WeekNumberWithinMonth.Second )) { result.Add( 2 ); }
        if (weekNums.HasFlag( WeekNumberWithinMonth.Third )) { result.Add( 3 ); }
        if (weekNums.HasFlag( WeekNumberWithinMonth.Fourth )) { result.Add( 4 ); }
        if (weekNums.HasFlag( WeekNumberWithinMonth.Fifth )) { result.Add( 5 ); }
        if (weekNums.HasFlag( WeekNumberWithinMonth.Sixth )) { result.Add( 6 ); }
        return [.. result];
    }

    /// <summary>
    /// Formats <see cref="WeekNumberWithinMonth"/> flags as a human-readable string using contiguous ranges.
    /// </summary>
    /// <param name="weekNums">The flags to format.</param>
    /// <param name="abbreviated">
    /// When <see langword="true"/>, uses numeric representation (e.g., "1").
    /// When <see langword="false"/>, uses ordinal names (e.g., "First").
    /// </param>
    public static string ToString( this WeekNumberWithinMonth weekNums, bool abbreviated = false )
        => RangeOfWeekNums.ToString( RangeOfWeekNums.GetContiguousRanges( weekNums ), abbreviated );

    #endregion WeekNumberWithinMonth
}
