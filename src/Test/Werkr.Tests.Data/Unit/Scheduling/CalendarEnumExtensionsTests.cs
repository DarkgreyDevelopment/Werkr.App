using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for the <see cref="CalendarEnumExtensions"/> class, validating conversion and ordering of <see
/// cref="DaysOfWeek"/>, <see cref="MonthsOfYear"/>, and <see cref="WeekNumberWithinMonth"/> flag enums.
/// </summary>
[TestClass]
public class CalendarEnumExtensionsTests {

    #region DaysOfWeek / DayOfWeek

    /// <summary>
    /// Verifies that all seven day flags produce a seven-element list ordered Monday through Sunday.
    /// </summary>
    [TestMethod]
    public void GetDaysOfWeek_AllDays_ReturnsSevenDays( ) {
        DaysOfWeek allDays = (DaysOfWeek)127;
        List<DayOfWeek> result = allDays.GetDaysOfWeek();
        Assert.HasCount(
            7,
            result
        );
        Assert.AreEqual(
            DayOfWeek.Monday,
            result[0]
        );
        Assert.AreEqual(
            DayOfWeek.Sunday,
            result[6]
        );
    }

    /// <summary>
    /// Verifies that <see cref="DaysOfWeek.None"/> produces an empty list.
    /// </summary>
    [TestMethod]
    public void GetDaysOfWeek_None_ReturnsEmptyList( ) {
        DaysOfWeek none = DaysOfWeek.None;
        List<DayOfWeek> result = none.GetDaysOfWeek();
        Assert.IsEmpty( result );
    }

    /// <summary>
    /// Verifies that a single-day flag returns a one-element list with the correct day.
    /// </summary>
    [TestMethod]
    public void GetDaysOfWeek_SingleDay_ReturnsSingleDay( ) {
        DaysOfWeek wednesday = DaysOfWeek.Wednesday;
        List<DayOfWeek> result = wednesday.GetDaysOfWeek();
        Assert.HasCount(
            1,
            result
        );
        Assert.AreEqual(
            DayOfWeek.Wednesday,
            result[0]
        );
    }

    /// <summary>
    /// Verifies that Monday | Wednesday | Friday flags return exactly those three days in order.
    /// </summary>
    [TestMethod]
    public void GetDaysOfWeek_MWF_ReturnsThreeDays( ) {
        DaysOfWeek mwf = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday;
        List<DayOfWeek> result = mwf.GetDaysOfWeek();
        Assert.HasCount(
            3,
            result
        );
        Assert.AreEqual(
            DayOfWeek.Monday,
            result[0]
        );
        Assert.AreEqual(
            DayOfWeek.Wednesday,
            result[1]
        );
        Assert.AreEqual(
            DayOfWeek.Friday,
            result[2]
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetWeekOfDays"/> returns seven days starting from the configured week start day
    /// (Monday).
    /// </summary>
    [TestMethod]
    public void GetWeekOfDays_ReturnsSevenDaysStartingFromWeekStartDay( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Monday;
            List<DayOfWeek> result = CalendarEnumExtensions.GetWeekOfDays();
            Assert.HasCount(
                7,
                result
            );
            Assert.AreEqual(
                DayOfWeek.Monday,
                result[0]
            );
            Assert.AreEqual(
                DayOfWeek.Sunday,
                result[6]
            );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    /// <summary>
    /// Verifies that <see cref="GetWeekOfDays"/> starts with Sunday when the week start is set to Sunday.
    /// </summary>
    [TestMethod]
    public void GetWeekOfDays_SundayStart_ReturnsSundayFirst( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Sunday;
            List<DayOfWeek> result = CalendarEnumExtensions.GetWeekOfDays();
            Assert.HasCount(
                7,
                result
            );
            Assert.AreEqual(
                DayOfWeek.Sunday,
                result[0]
            );
            Assert.AreEqual(
                DayOfWeek.Saturday,
                result[6]
            );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    /// <summary>
    /// Verifies that <see cref="OrderDaysOfWeek"/> reorders days relative to the configured start day.
    /// </summary>
    [TestMethod]
    public void OrderDaysOfWeek_ReordersFromStartDay( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Wednesday;
            List<DayOfWeek> days = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
            List<DayOfWeek> result = days.OrderDaysOfWeek();
            Assert.HasCount(
                3,
                result
            );
            Assert.AreEqual(
                DayOfWeek.Wednesday,
                result[0]
            );
            Assert.AreEqual(
                DayOfWeek.Friday,
                result[1]
            );
            Assert.AreEqual(
                DayOfWeek.Monday,
                result[2]
            );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    /// <summary>
    /// Verifies that inclusive mode includes the specified start day in the remaining days.
    /// </summary>
    [TestMethod]
    public void GetRemainingDaysInWeek_Inclusive_IncludesStartDay( ) {
        List<DayOfWeek> allDays = CalendarEnumExtensions.GetUnorderedWeekOfDays();
        List<DayOfWeek> result = allDays.GetRemainingDaysInWeek(
            DayOfWeek.Wednesday,
            exclusive: false
        );
        Assert.Contains(
            DayOfWeek.Wednesday,
            result
        );
    }

    /// <summary>
    /// Verifies that exclusive mode excludes the specified start day from the remaining days.
    /// </summary>
    [TestMethod]
    public void GetRemainingDaysInWeek_Exclusive_ExcludesStartDay( ) {
        List<DayOfWeek> allDays = CalendarEnumExtensions.GetUnorderedWeekOfDays();
        List<DayOfWeek> result = allDays.GetRemainingDaysInWeek(
            DayOfWeek.Wednesday,
            exclusive: true
        );
        Assert.DoesNotContain(
            DayOfWeek.Wednesday,
            result
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetNextDayInWeek"/> returns the next matching day after the given day.
    /// </summary>
    [TestMethod]
    public void GetNextDayInWeek_ReturnsNextMatchingDay( ) {
        List<DayOfWeek> mwf = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
        DayOfWeek? result = mwf.GetNextDayInWeek( DayOfWeek.Monday );
        Assert.AreEqual(
            DayOfWeek.Wednesday,
            result
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetNextDayInWeek"/> returns <see langword="null"/> when the given day is the last in
    /// the week.
    /// </summary>
    [TestMethod]
    public void GetNextDayInWeek_LastDay_ReturnsNull( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Monday;
            List<DayOfWeek> days = [DayOfWeek.Monday, DayOfWeek.Sunday];
            DayOfWeek? result = days.GetNextDayInWeek( DayOfWeek.Sunday );
            Assert.IsNull( result );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    /// <summary>
    /// Verifies that <see cref="ToString"/> with abbreviated false returns the full day name.
    /// </summary>
    [TestMethod]
    public void ToString_DaysOfWeek_AbbreviatedFalse_ReturnsFullNames( ) {
        DaysOfWeek monday = DaysOfWeek.Monday;
        string result = monday.ToString( abbreviated: false );
        Assert.Contains(
            "Monday",
            result
        );
    }

    /// <summary>
    /// Verifies that <see cref="ToString"/> with abbreviated true returns abbreviated day names.
    /// </summary>
    [TestMethod]
    public void ToString_DaysOfWeek_AbbreviatedTrue_ReturnsShortNames( ) {
        DaysOfWeek monday = DaysOfWeek.Monday;
        string result = monday.ToString( abbreviated: true );
        Assert.Contains(
            "Mon",
            result
        );
    }

    #endregion DaysOfWeek / DayOfWeek

    #region MonthsOfYear / Month

    /// <summary>
    /// Verifies that all twelve month flags produce a twelve-element list ordered January through December.
    /// </summary>
    [TestMethod]
    public void GetMonths_AllMonths_ReturnsTwelveMonths( ) {
        MonthsOfYear allMonths = (MonthsOfYear)65520;
        List<Month> result = allMonths.GetMonths();
        Assert.HasCount(
            12,
            result
        );
        Assert.AreEqual(
            Month.January,
            result[0]
        );
        Assert.AreEqual(
            Month.December,
            result[11]
        );
    }

    /// <summary>
    /// Verifies that quarterly month flags convert to their correct integer representations.
    /// </summary>
    [TestMethod]
    public void GetIntMonths_QuarterlyMonths_ReturnsFourInts( ) {
        MonthsOfYear quarterly = MonthsOfYear.January | MonthsOfYear.April | MonthsOfYear.July | MonthsOfYear.October;
        int[] result = quarterly.GetIntMonths();
        Assert.HasCount(
            4,
            result
        );
        Assert.AreEqual(
            1,
            result[0]
        );
        Assert.AreEqual(
            4,
            result[1]
        );
        Assert.AreEqual(
            7,
            result[2]
        );
        Assert.AreEqual(
            10,
            result[3]
        );
    }

    /// <summary>
    /// Verifies that inclusive mode includes the start month in the remaining months.
    /// </summary>
    [TestMethod]
    public void GetRemainingMonthsInYear_Inclusive_IncludesStartMonth( ) {
        int[] months = [1, 4, 7, 10];
        int[] result = months.GetRemainingMonthsInYear(
            4,
            exclusive: false
        );
        CollectionAssert.Contains(
            result,
            4
        );
    }

    /// <summary>
    /// Verifies that exclusive mode excludes the start month and returns only later months.
    /// </summary>
    [TestMethod]
    public void GetRemainingMonthsInYear_Exclusive_ExcludesStartMonth( ) {
        int[] months = [1, 4, 7, 10];
        int[] result = months.GetRemainingMonthsInYear(
            4,
            exclusive: true
        );
        CollectionAssert.DoesNotContain(
            result,
            4
        );
        Assert.HasCount(
            2,
            result
        );
    }

    /// <summary>
    /// Verifies that converting months to flags and back preserves the original values.
    /// </summary>
    [TestMethod]
    public void GetMonths_RoundTrip_PreservesValues( ) {
        List<Month> original = [Month.March, Month.July, Month.November];
        MonthsOfYear flags = original.GetMonths();
        List<Month> roundTripped = flags.GetMonths();
        CollectionAssert.AreEqual(
            original,
            roundTripped
        );
    }

    /// <summary>
    /// Verifies that <see cref="ToString"/> with abbreviated false returns the full month name.
    /// </summary>
    [TestMethod]
    public void ToString_MonthsOfYear_FullName_ContainsMonthName( ) {
        MonthsOfYear january = MonthsOfYear.January;
        string result = january.ToString( abbreviated: false );
        Assert.Contains(
            "January",
            result
        );
    }

    /// <summary>
    /// Verifies that <see cref="ToString"/> with abbreviated true returns abbreviated month names.
    /// </summary>
    [TestMethod]
    public void ToString_MonthsOfYear_Abbreviated_ContainsShortName( ) {
        MonthsOfYear january = MonthsOfYear.January;
        string result = january.ToString( abbreviated: true );
        Assert.Contains(
            "Jan",
            result
        );
    }

    #endregion MonthsOfYear / Month

    #region WeekNumberWithinMonth

    /// <summary>
    /// Verifies that all six week-number flags convert to a six-element array (1 through 6).
    /// </summary>
    [TestMethod]
    public void GetWeekNumbersInMonth_AllWeeks_ReturnsSix( ) {
        WeekNumberWithinMonth allWeeks = (WeekNumberWithinMonth)63;
        int[] result = allWeeks.GetWeekNumbersInMonth();
        Assert.HasCount(
            6,
            result
        );
        Assert.AreEqual(
            1,
            result[0]
        );
        Assert.AreEqual(
            6,
            result[5]
        );
    }

    /// <summary>
    /// Verifies that <see cref="WeekNumberWithinMonth.None"/> produces an empty array.
    /// </summary>
    [TestMethod]
    public void GetWeekNumbersInMonth_None_ReturnsEmpty( ) {
        WeekNumberWithinMonth none = WeekNumberWithinMonth.None;
        int[] result = none.GetWeekNumbersInMonth();
        Assert.IsEmpty( result );
    }

    /// <summary>
    /// Verifies that First | Third flags return only week numbers 1 and 3.
    /// </summary>
    [TestMethod]
    public void GetWeekNumbersInMonth_FirstAndThird_ReturnsTwoWeeks( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Third;
        int[] result = weeks.GetWeekNumbersInMonth();
        Assert.HasCount(
            2,
            result
        );
        Assert.AreEqual(
            1,
            result[0]
        );
        Assert.AreEqual(
            3,
            result[1]
        );
    }

    /// <summary>
    /// Verifies that abbreviated <see cref="ToString"/> returns a numeric representation for week numbers.
    /// </summary>
    [TestMethod]
    public void ToString_WeekNumberWithinMonth_Abbreviated_ReturnsNumeric( ) {
        WeekNumberWithinMonth first = WeekNumberWithinMonth.First;
        string result = first.ToString( abbreviated: true );
        Assert.Contains(
            "1",
            result
        );
    }

    /// <summary>
    /// Verifies that full-name <see cref="ToString"/> returns ordinal text for week numbers.
    /// </summary>
    [TestMethod]
    public void ToString_WeekNumberWithinMonth_FullName_ReturnsOrdinal( ) {
        WeekNumberWithinMonth first = WeekNumberWithinMonth.First;
        string result = first.ToString( abbreviated: false );
        Assert.Contains(
            "First",
            result
        );
    }

    #endregion WeekNumberWithinMonth

}
