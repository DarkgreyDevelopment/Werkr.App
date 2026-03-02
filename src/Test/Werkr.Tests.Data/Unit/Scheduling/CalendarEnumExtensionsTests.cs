using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class CalendarEnumExtensionsTests {

    #region DaysOfWeek / DayOfWeek

    [TestMethod]
    public void GetDaysOfWeek_AllDays_ReturnsSevenDays( ) {
        DaysOfWeek allDays = (DaysOfWeek)127;
        List<DayOfWeek> result = allDays.GetDaysOfWeek();
        Assert.HasCount( 7, result );
        Assert.AreEqual( DayOfWeek.Monday, result[0] );
        Assert.AreEqual( DayOfWeek.Sunday, result[6] );
    }

    [TestMethod]
    public void GetDaysOfWeek_None_ReturnsEmptyList( ) {
        DaysOfWeek none = DaysOfWeek.None;
        List<DayOfWeek> result = none.GetDaysOfWeek();
        Assert.IsEmpty( result );
    }

    [TestMethod]
    public void GetDaysOfWeek_SingleDay_ReturnsSingleDay( ) {
        DaysOfWeek wednesday = DaysOfWeek.Wednesday;
        List<DayOfWeek> result = wednesday.GetDaysOfWeek();
        Assert.HasCount( 1, result );
        Assert.AreEqual( DayOfWeek.Wednesday, result[0] );
    }

    [TestMethod]
    public void GetDaysOfWeek_MWF_ReturnsThreeDays( ) {
        DaysOfWeek mwf = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday;
        List<DayOfWeek> result = mwf.GetDaysOfWeek();
        Assert.HasCount( 3, result );
        Assert.AreEqual( DayOfWeek.Monday, result[0] );
        Assert.AreEqual( DayOfWeek.Wednesday, result[1] );
        Assert.AreEqual( DayOfWeek.Friday, result[2] );
    }

    [TestMethod]
    public void GetWeekOfDays_ReturnsSevenDaysStartingFromWeekStartDay( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Monday;
            List<DayOfWeek> result = CalendarEnumExtensions.GetWeekOfDays();
            Assert.HasCount( 7, result );
            Assert.AreEqual( DayOfWeek.Monday, result[0] );
            Assert.AreEqual( DayOfWeek.Sunday, result[6] );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    [TestMethod]
    public void GetWeekOfDays_SundayStart_ReturnsSundayFirst( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Sunday;
            List<DayOfWeek> result = CalendarEnumExtensions.GetWeekOfDays();
            Assert.HasCount( 7, result );
            Assert.AreEqual( DayOfWeek.Sunday, result[0] );
            Assert.AreEqual( DayOfWeek.Saturday, result[6] );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    [TestMethod]
    public void OrderDaysOfWeek_ReordersFromStartDay( ) {
        DayOfWeek savedStart = CalendarEnumExtensions.WeekStartDay;
        try {
            CalendarEnumExtensions.WeekStartDay = DayOfWeek.Wednesday;
            List<DayOfWeek> days = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
            List<DayOfWeek> result = days.OrderDaysOfWeek();
            Assert.HasCount( 3, result );
            Assert.AreEqual( DayOfWeek.Wednesday, result[0] );
            Assert.AreEqual( DayOfWeek.Friday, result[1] );
            Assert.AreEqual( DayOfWeek.Monday, result[2] );
        } finally {
            CalendarEnumExtensions.WeekStartDay = savedStart;
        }
    }

    [TestMethod]
    public void GetRemainingDaysInWeek_Inclusive_IncludesStartDay( ) {
        List<DayOfWeek> allDays = CalendarEnumExtensions.GetUnorderedWeekOfDays();
        List<DayOfWeek> result = allDays.GetRemainingDaysInWeek( DayOfWeek.Wednesday, exclusive: false );
        Assert.Contains( DayOfWeek.Wednesday, result );
    }

    [TestMethod]
    public void GetRemainingDaysInWeek_Exclusive_ExcludesStartDay( ) {
        List<DayOfWeek> allDays = CalendarEnumExtensions.GetUnorderedWeekOfDays();
        List<DayOfWeek> result = allDays.GetRemainingDaysInWeek( DayOfWeek.Wednesday, exclusive: true );
        Assert.DoesNotContain( DayOfWeek.Wednesday, result );
    }

    [TestMethod]
    public void GetNextDayInWeek_ReturnsNextMatchingDay( ) {
        List<DayOfWeek> mwf = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday];
        DayOfWeek? result = mwf.GetNextDayInWeek( DayOfWeek.Monday );
        Assert.AreEqual( DayOfWeek.Wednesday, result );
    }

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

    [TestMethod]
    public void ToString_DaysOfWeek_AbbreviatedFalse_ReturnsFullNames( ) {
        DaysOfWeek monday = DaysOfWeek.Monday;
        string result = monday.ToString( abbreviated: false );
        Assert.Contains( "Monday", result );
    }

    [TestMethod]
    public void ToString_DaysOfWeek_AbbreviatedTrue_ReturnsShortNames( ) {
        DaysOfWeek monday = DaysOfWeek.Monday;
        string result = monday.ToString( abbreviated: true );
        Assert.Contains( "Mon", result );
    }

    #endregion DaysOfWeek / DayOfWeek


    #region MonthsOfYear / Month

    [TestMethod]
    public void GetMonths_AllMonths_ReturnsTwelveMonths( ) {
        MonthsOfYear allMonths = (MonthsOfYear)65520;
        List<Month> result = allMonths.GetMonths();
        Assert.HasCount( 12, result );
        Assert.AreEqual( Month.January, result[0] );
        Assert.AreEqual( Month.December, result[11] );
    }

    [TestMethod]
    public void GetIntMonths_QuarterlyMonths_ReturnsFourInts( ) {
        MonthsOfYear quarterly = MonthsOfYear.January | MonthsOfYear.April | MonthsOfYear.July | MonthsOfYear.October;
        int[] result = quarterly.GetIntMonths();
        Assert.HasCount( 4, result );
        Assert.AreEqual( 1, result[0] );
        Assert.AreEqual( 4, result[1] );
        Assert.AreEqual( 7, result[2] );
        Assert.AreEqual( 10, result[3] );
    }

    [TestMethod]
    public void GetRemainingMonthsInYear_Inclusive_IncludesStartMonth( ) {
        int[] months = [1, 4, 7, 10];
        int[] result = months.GetRemainingMonthsInYear( 4, exclusive: false );
        CollectionAssert.Contains( result, 4 );
    }

    [TestMethod]
    public void GetRemainingMonthsInYear_Exclusive_ExcludesStartMonth( ) {
        int[] months = [1, 4, 7, 10];
        int[] result = months.GetRemainingMonthsInYear( 4, exclusive: true );
        CollectionAssert.DoesNotContain( result, 4 );
        Assert.HasCount( 2, result );
    }

    [TestMethod]
    public void GetMonths_RoundTrip_PreservesValues( ) {
        List<Month> original = [Month.March, Month.July, Month.November];
        MonthsOfYear flags = original.GetMonths();
        List<Month> roundTripped = flags.GetMonths();
        CollectionAssert.AreEqual( original, roundTripped );
    }

    [TestMethod]
    public void ToString_MonthsOfYear_FullName_ContainsMonthName( ) {
        MonthsOfYear january = MonthsOfYear.January;
        string result = january.ToString( abbreviated: false );
        Assert.Contains( "January", result );
    }

    [TestMethod]
    public void ToString_MonthsOfYear_Abbreviated_ContainsShortName( ) {
        MonthsOfYear january = MonthsOfYear.January;
        string result = january.ToString( abbreviated: true );
        Assert.Contains( "Jan", result );
    }

    #endregion MonthsOfYear / Month


    #region WeekNumberWithinMonth

    [TestMethod]
    public void GetWeekNumbersInMonth_AllWeeks_ReturnsSix( ) {
        WeekNumberWithinMonth allWeeks = (WeekNumberWithinMonth)63;
        int[] result = allWeeks.GetWeekNumbersInMonth();
        Assert.HasCount( 6, result );
        Assert.AreEqual( 1, result[0] );
        Assert.AreEqual( 6, result[5] );
    }

    [TestMethod]
    public void GetWeekNumbersInMonth_None_ReturnsEmpty( ) {
        WeekNumberWithinMonth none = WeekNumberWithinMonth.None;
        int[] result = none.GetWeekNumbersInMonth();
        Assert.IsEmpty( result );
    }

    [TestMethod]
    public void GetWeekNumbersInMonth_FirstAndThird_ReturnsTwoWeeks( ) {
        WeekNumberWithinMonth weeks = WeekNumberWithinMonth.First | WeekNumberWithinMonth.Third;
        int[] result = weeks.GetWeekNumbersInMonth();
        Assert.HasCount( 2, result );
        Assert.AreEqual( 1, result[0] );
        Assert.AreEqual( 3, result[1] );
    }

    [TestMethod]
    public void ToString_WeekNumberWithinMonth_Abbreviated_ReturnsNumeric( ) {
        WeekNumberWithinMonth first = WeekNumberWithinMonth.First;
        string result = first.ToString( abbreviated: true );
        Assert.Contains( "1", result );
    }

    [TestMethod]
    public void ToString_WeekNumberWithinMonth_FullName_ReturnsOrdinal( ) {
        WeekNumberWithinMonth first = WeekNumberWithinMonth.First;
        string result = first.ToString( abbreviated: false );
        Assert.Contains( "First", result );
    }

    #endregion WeekNumberWithinMonth

}
