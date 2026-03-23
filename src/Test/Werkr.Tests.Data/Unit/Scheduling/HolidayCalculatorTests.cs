using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class HolidayCalculatorTests {

    #region Helpers

    private static HolidayRule MakeFixedDateRule(
        int month, int day,
        ObservanceRule observance = ObservanceRule.None,
        int? yearStart = null, int? yearEnd = null,
        TimeOnly? windowStart = null, TimeOnly? windowEnd = null,
        string? windowTz = null ) => new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = $"FixedDate {month}/{day}",
            RuleType = HolidayRuleType.FixedDate,
            Month = month,
            Day = day,
            ObservanceRule = observance,
            YearStart = yearStart,
            YearEnd = yearEnd,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            WindowTimeZoneId = windowTz,
        };

    private static HolidayRule MakeNthWeekdayRule(
        int month, DayOfWeek dayOfWeek, int weekNumber,
        int? yearStart = null, int? yearEnd = null ) => new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = $"Nth {dayOfWeek} #{weekNumber} in month {month}",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = month,
            DayOfWeek = dayOfWeek,
            WeekNumber = weekNumber,
            YearStart = yearStart,
            YearEnd = yearEnd,
        };

    private static HolidayRule MakeLastWeekdayRule(
        int month, DayOfWeek dayOfWeek,
        int? yearStart = null, int? yearEnd = null ) => new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = $"Last {dayOfWeek} in month {month}",
            RuleType = HolidayRuleType.LastWeekdayOfMonth,
            Month = month,
            DayOfWeek = dayOfWeek,
            YearStart = yearStart,
            YearEnd = yearEnd,
        };

    private static HolidayCalendar MakeCalendar( params HolidayRule[] rules ) {
        Guid calId = Guid.NewGuid( );
        foreach (HolidayRule r in rules) {
            r.HolidayCalendarId = calId;
        }

        return new HolidayCalendar {
            Id = calId,
            Name = "Test Calendar",
            Rules = rules,
        };
    }

    #endregion

    // ── FixedDate ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void FixedDate_NewYearsDay_Returns_Jan1( ) {
        HolidayRule rule = MakeFixedDateRule( 1, 1 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 1, 1 ), dates[0].Date );
    }

    [TestMethod]
    public void FixedDate_July4_Returns_July4( ) {
        HolidayRule rule = MakeFixedDateRule( 7, 4 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 7, 4 ), dates[0].Date );
    }

    [TestMethod]
    public void FixedDate_Dec25_Returns_Christmas( ) {
        HolidayRule rule = MakeFixedDateRule( 12, 25 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2025 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2025, 12, 25 ), dates[0].Date );
    }

    // ── Observance Rules ───────────────────────────────────────────────────────

    [TestMethod]
    public void Observance_SatToFri_SunToMon_ShiftsSaturday( ) {
        // July 4 2026 = Saturday
        HolidayRule rule = MakeFixedDateRule( 7, 4, ObservanceRule.SaturdayToFriday_SundayToMonday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        // Saturday → Friday July 3
        Assert.AreEqual( new DateOnly( 2026, 7, 3 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_SatToFri_SunToMon_ShiftsSunday( ) {
        // Jan 1 2023 = Sunday
        HolidayRule rule = MakeFixedDateRule( 1, 1, ObservanceRule.SaturdayToFriday_SundayToMonday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2023 );
        Assert.HasCount( 1, dates );
        // Sunday → Monday Jan 2
        Assert.AreEqual( new DateOnly( 2023, 1, 2 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_SatToMon_ShiftsSaturday( ) {
        // July 4 2026 = Saturday
        HolidayRule rule = MakeFixedDateRule( 7, 4, ObservanceRule.SaturdayToMonday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        // Saturday → Monday July 6
        Assert.AreEqual( new DateOnly( 2026, 7, 6 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_SatToMon_NoShiftOnSunday( ) {
        // Jan 1 2023 = Sunday
        HolidayRule rule = MakeFixedDateRule( 1, 1, ObservanceRule.SaturdayToMonday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2023 );
        Assert.HasCount( 1, dates );
        // Sunday is NOT shifted by SaturdayToMonday rule
        Assert.AreEqual( new DateOnly( 2023, 1, 1 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_NearestWeekday_ShiftsSaturday( ) {
        // July 4 2026 = Saturday
        HolidayRule rule = MakeFixedDateRule( 7, 4, ObservanceRule.NearestWeekday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        // Saturday → Friday July 3
        Assert.AreEqual( new DateOnly( 2026, 7, 3 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_NearestWeekday_ShiftsSunday( ) {
        // Jan 1 2023 = Sunday
        HolidayRule rule = MakeFixedDateRule( 1, 1, ObservanceRule.NearestWeekday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2023 );
        Assert.HasCount( 1, dates );
        // Sunday → Monday Jan 2
        Assert.AreEqual( new DateOnly( 2023, 1, 2 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_None_NoShift( ) {
        // July 4 2026 = Saturday
        HolidayRule rule = MakeFixedDateRule( 7, 4, ObservanceRule.None );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 7, 4 ), dates[0].Date );
    }

    [TestMethod]
    public void Observance_WeekdayNoShift( ) {
        // July 4 2025 = Friday — no shift needed
        HolidayRule rule = MakeFixedDateRule( 7, 4, ObservanceRule.SaturdayToFriday_SundayToMonday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2025 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2025, 7, 4 ), dates[0].Date );
    }

    // ── NthWeekdayOfMonth ──────────────────────────────────────────────────────

    [TestMethod]
    public void NthWeekday_ThirdMondayJan_MLK_2026( ) {
        // MLK Day = 3rd Monday in January
        HolidayRule rule = MakeNthWeekdayRule( 1, DayOfWeek.Monday, 3 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 1, 19 ), dates[0].Date );
    }

    [TestMethod]
    public void NthWeekday_ThirdMondayFeb_PresidentsDay_2026( ) {
        // Presidents' Day = 3rd Monday in February
        HolidayRule rule = MakeNthWeekdayRule( 2, DayOfWeek.Monday, 3 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 2, 16 ), dates[0].Date );
    }

    [TestMethod]
    public void NthWeekday_FirstMondaySept_LaborDay_2026( ) {
        // Labor Day = 1st Monday in September
        HolidayRule rule = MakeNthWeekdayRule( 9, DayOfWeek.Monday, 1 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 9, 7 ), dates[0].Date );
    }

    [TestMethod]
    public void NthWeekday_SecondMondayOct_ColumbusDay_2026( ) {
        // Columbus Day = 2nd Monday in October
        HolidayRule rule = MakeNthWeekdayRule( 10, DayOfWeek.Monday, 2 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 10, 12 ), dates[0].Date );
    }

    [TestMethod]
    public void NthWeekday_FourthThursdayNov_Thanksgiving_2026( ) {
        // Thanksgiving = 4th Thursday in November
        HolidayRule rule = MakeNthWeekdayRule( 11, DayOfWeek.Thursday, 4 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 11, 26 ), dates[0].Date );
    }

    [TestMethod]
    public void NthWeekday_FifthMondayReturnsEmpty_WhenNotEnough( ) {
        // 5th Monday of February 2026 — does not exist
        HolidayRule rule = MakeNthWeekdayRule( 2, DayOfWeek.Monday, 5 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 0, dates );
    }

    // ── LastWeekdayOfMonth ─────────────────────────────────────────────────────

    [TestMethod]
    public void LastWeekday_LastMondayMay_MemorialDay_2026( ) {
        // Memorial Day = Last Monday in May
        HolidayRule rule = MakeLastWeekdayRule( 5, DayOfWeek.Monday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 5, 25 ), dates[0].Date );
    }

    [TestMethod]
    public void LastWeekday_LastFridayOfJune_2026( ) {
        HolidayRule rule = MakeLastWeekdayRule( 6, DayOfWeek.Friday );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2026, 6, 26 ), dates[0].Date );
    }

    // ── Year Bounds ────────────────────────────────────────────────────────────

    [TestMethod]
    public void YearBounds_Before_YearStart_ReturnsEmpty( ) {
        HolidayRule rule = MakeFixedDateRule( 7, 4, yearStart: 2026 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2025 );
        Assert.HasCount( 0, dates );
    }

    [TestMethod]
    public void YearBounds_After_YearEnd_ReturnsEmpty( ) {
        HolidayRule rule = MakeFixedDateRule( 7, 4, yearEnd: 2025 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 0, dates );
    }

    [TestMethod]
    public void YearBounds_AtBoundary_Inclusive( ) {
        HolidayRule rule = MakeFixedDateRule( 7, 4, yearStart: 2025, yearEnd: 2027 );

        Assert.HasCount( 1, HolidayCalculator.ComputeDatesForYear( rule, 2025 ) );
        Assert.HasCount( 1, HolidayCalculator.ComputeDatesForYear( rule, 2026 ) );
        Assert.HasCount( 1, HolidayCalculator.ComputeDatesForYear( rule, 2027 ) );
    }

    [TestMethod]
    public void YearBounds_Null_NoRestriction( ) {
        HolidayRule rule = MakeFixedDateRule( 7, 4 );
        Assert.HasCount( 1, HolidayCalculator.ComputeDatesForYear( rule, 1900 ) );
        Assert.HasCount( 1, HolidayCalculator.ComputeDatesForYear( rule, 2100 ) );
    }

    // ── Leap Year & Edge Cases ─────────────────────────────────────────────────

    [TestMethod]
    public void LeapYear_Feb29_LeapYear_Returns( ) {
        HolidayRule rule = MakeFixedDateRule( 2, 29 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2024 );
        Assert.HasCount( 1, dates );
        Assert.AreEqual( new DateOnly( 2024, 2, 29 ), dates[0].Date );
    }

    [TestMethod]
    public void LeapYear_Feb29_NonLeapYear_ReturnsEmpty( ) {
        HolidayRule rule = MakeFixedDateRule( 2, 29 );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2025 );
        Assert.HasCount( 0, dates );
    }

    [TestMethod]
    public void FixedDate_MissingMonth_ReturnsEmpty( ) {
        HolidayRule rule = new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = "No Month",
            RuleType = HolidayRuleType.FixedDate,
            Day = 1,
        };
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 0, dates );
    }

    [TestMethod]
    public void FixedDate_MissingDay_ReturnsEmpty( ) {
        HolidayRule rule = new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = "No Day",
            RuleType = HolidayRuleType.FixedDate,
            Month = 1,
        };
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 0, dates );
    }

    [TestMethod]
    public void NthWeekday_MissingDayOfWeek_ReturnsEmpty( ) {
        HolidayRule rule = new( ) {
            HolidayCalendarId = Guid.NewGuid( ),
            Name = "No DayOfWeek",
            RuleType = HolidayRuleType.NthWeekdayOfMonth,
            Month = 1,
            WeekNumber = 3,
        };
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2026 );
        Assert.HasCount( 0, dates );
    }

    // ── Time Window Inheritance ────────────────────────────────────────────────

    [TestMethod]
    public void FixedDate_WindowInherited( ) {
        TimeOnly start = new( 9, 30 );
        TimeOnly end = new( 16, 0 );
        HolidayRule rule = MakeFixedDateRule( 7, 4, windowStart: start, windowEnd: end, windowTz: "America/New_York" );
        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForYear( rule, 2025 );

        Assert.HasCount( 1, dates );
        Assert.AreEqual( start, dates[0].WindowStart );
        Assert.AreEqual( end, dates[0].WindowEnd );
        Assert.AreEqual( "America/New_York", dates[0].WindowTimeZoneId );
    }

    // ── Batch / Range Computation ──────────────────────────────────────────────

    [TestMethod]
    public void ComputeAllDatesForYear_MultipleRules( ) {
        HolidayCalendar cal = MakeCalendar(
            MakeFixedDateRule( 1, 1 ),
            MakeFixedDateRule( 7, 4 ),
            MakeFixedDateRule( 12, 25 ) );

        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeAllDatesForYear( cal, 2026 );
        Assert.HasCount( 3, dates );
    }

    [TestMethod]
    public void ComputeDatesForRange_MultiYear( ) {
        HolidayCalendar cal = MakeCalendar(
            MakeFixedDateRule( 1, 1 ),
            MakeFixedDateRule( 7, 4 ) );

        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForRange( cal, 2025, 2027 );
        // 2 holidays × 3 years = 6
        Assert.HasCount( 6, dates );
    }

    // ── US Federal Holidays 2026 ───────────────────────────────────────────────

    [TestMethod]
    public void USFederalHolidays_2026_AllCorrect( ) {
        ObservanceRule obs = ObservanceRule.SaturdayToFriday_SundayToMonday;
        HolidayCalendar cal = MakeCalendar(
            MakeFixedDateRule( 1, 1, obs ),      // New Year's Day
            MakeNthWeekdayRule( 1, DayOfWeek.Monday, 3 ),  // MLK
            MakeNthWeekdayRule( 2, DayOfWeek.Monday, 3 ),  // Presidents' Day
            MakeLastWeekdayRule( 5, DayOfWeek.Monday ),     // Memorial Day
            MakeFixedDateRule( 6, 19, obs ),     // Juneteenth
            MakeFixedDateRule( 7, 4, obs ),      // Independence Day
            MakeNthWeekdayRule( 9, DayOfWeek.Monday, 1 ),  // Labor Day
            MakeNthWeekdayRule( 10, DayOfWeek.Monday, 2 ), // Columbus Day
            MakeFixedDateRule( 11, 11, obs ),    // Veterans Day
            MakeNthWeekdayRule( 11, DayOfWeek.Thursday, 4 ), // Thanksgiving
            MakeFixedDateRule( 12, 25, obs ) );  // Christmas

        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeAllDatesForYear( cal, 2026 );
        Assert.HasCount( 11, dates );

        DateOnly[] expected = [
            new( 2026, 1, 1 ),   // New Year's Day (Thursday)
            new( 2026, 1, 19 ),  // MLK Day (Monday)
            new( 2026, 2, 16 ),  // Presidents' Day (Monday)
            new( 2026, 5, 25 ),  // Memorial Day (Monday)
            new( 2026, 6, 19 ),  // Juneteenth (Friday)
            new( 2026, 7, 3 ),   // Independence Day observed (Saturday → Friday)
            new( 2026, 9, 7 ),   // Labor Day (Monday)
            new( 2026, 10, 12 ), // Columbus Day (Monday)
            new( 2026, 11, 11 ), // Veterans Day (Wednesday)
            new( 2026, 11, 26 ), // Thanksgiving (Thursday)
            new( 2026, 12, 25 ), // Christmas (Friday)
        ];

        DateOnly[] actual = [.. dates.Select( d => d.Date ).OrderBy( d => d )];
        CollectionAssert.AreEqual( expected, actual );
    }

    [TestMethod]
    public void USFederalHolidays_2027_AllCorrect( ) {
        ObservanceRule obs = ObservanceRule.SaturdayToFriday_SundayToMonday;
        HolidayCalendar cal = MakeCalendar(
            MakeFixedDateRule( 1, 1, obs ),
            MakeNthWeekdayRule( 1, DayOfWeek.Monday, 3 ),
            MakeNthWeekdayRule( 2, DayOfWeek.Monday, 3 ),
            MakeLastWeekdayRule( 5, DayOfWeek.Monday ),
            MakeFixedDateRule( 6, 19, obs ),
            MakeFixedDateRule( 7, 4, obs ),
            MakeNthWeekdayRule( 9, DayOfWeek.Monday, 1 ),
            MakeNthWeekdayRule( 10, DayOfWeek.Monday, 2 ),
            MakeFixedDateRule( 11, 11, obs ),
            MakeNthWeekdayRule( 11, DayOfWeek.Thursday, 4 ),
            MakeFixedDateRule( 12, 25, obs ) );

        IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeAllDatesForYear( cal, 2027 );
        Assert.HasCount( 11, dates );

        DateOnly[] expected = [
            new( 2027, 1, 1 ),   // New Year's Day (Friday)
            new( 2027, 1, 18 ),  // MLK Day (Monday)
            new( 2027, 2, 15 ),  // Presidents' Day (Monday)
            new( 2027, 5, 31 ),  // Memorial Day (Monday)
            new( 2027, 6, 18 ),  // Juneteenth observed (Saturday → Friday)
            new( 2027, 7, 5 ),   // Independence Day observed (Sunday → Monday)
            new( 2027, 9, 6 ),   // Labor Day (Monday)
            new( 2027, 10, 11 ), // Columbus Day (Monday)
            new( 2027, 11, 11 ), // Veterans Day (Thursday)
            new( 2027, 11, 25 ), // Thanksgiving (Thursday)
            new( 2027, 12, 24 ), // Christmas observed (Saturday → Friday)
        ];

        DateOnly[] actual = [.. dates.Select( d => d.Date ).OrderBy( d => d )];
        CollectionAssert.AreEqual( expected, actual );
    }

    // ── Internal Helpers ───────────────────────────────────────────────────────

    [TestMethod]
    public void GetNthWeekdayOfMonth_FirstMonday_Jan2026( ) {
        DateOnly? result = HolidayCalculator.GetNthWeekdayOfMonth( 2026, 1, DayOfWeek.Monday, 1 );
        Assert.AreEqual( new DateOnly( 2026, 1, 5 ), result );
    }

    [TestMethod]
    public void GetLastWeekdayOfMonth_LastMonday_May2026( ) {
        DateOnly result = HolidayCalculator.GetLastWeekdayOfMonth( 2026, 5, DayOfWeek.Monday );
        Assert.AreEqual( new DateOnly( 2026, 5, 25 ), result );
    }

    [TestMethod]
    public void ApplyObservanceRule_None_ReturnsUnchanged( ) {
        DateOnly saturday = new( 2026, 7, 4 );
        Assert.AreEqual( saturday, HolidayCalculator.ApplyObservanceRule( saturday, ObservanceRule.None ) );
    }
}
