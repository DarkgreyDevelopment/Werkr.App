using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class HolidayFilterTests {

    #region Helpers

    private static Schedule MakeDailySchedule( DateOnly startDate, TimeOnly startTime, string tzId = "UTC" ) => new( ) {
        DbSchedule = new DbSchedule { Name = "Test Daily", StopTaskAfterMinutes = 30 },
        StartDateTime = new StartDateTimeInfo {
            Date = startDate,
            Time = startTime,
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById( tzId ),
        },
        DailyRecurrence = new DailyRecurrence { DayInterval = 1 },
    };

    private static HolidayDate MakeFullDayHoliday( DateOnly date, string name = "Holiday" ) => new( ) {
        Date = date,
        Name = name,
        Year = date.Year,
    };

    private static HolidayDate MakeTimeWindowHoliday(
        DateOnly date, TimeOnly windowStart, TimeOnly windowEnd,
        string tzId = "America/New_York", string name = "Window Holiday" ) => new( ) {
            Date = date,
            Name = name,
            Year = date.Year,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            WindowTimeZoneId = tzId,
        };

    #endregion

    // ── Blocklist Mode ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Blocklist_SuppressesMatchingOccurrences( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 10, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ), "Independence Day" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        // July 1-9 is 9 days, minus July 4 = 8
        Assert.HasCount( 8, result.Occurrences );
        Assert.HasCount( 1, result.Suppressed );
        Assert.AreEqual( "Independence Day", result.Suppressed[0].HolidayName );
    }

    [TestMethod]
    public void Blocklist_NoMatch_KeepsAll( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 3, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 3, 5, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ) ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        Assert.HasCount( 4, result.Occurrences );
        Assert.HasCount( 0, result.Suppressed );
    }

    [TestMethod]
    public void Blocklist_MultipleHolidays( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 12, 23 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 12, 28, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 12, 25 ), "Christmas" ),
            MakeFullDayHoliday( new DateOnly( 2026, 12, 26 ), "Boxing Day" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        // Dec 23-27 = 5 days, minus 2 holidays = 3
        Assert.HasCount( 3, result.Occurrences );
        Assert.HasCount( 2, result.Suppressed );
    }

    // ── Allowlist Mode ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Allowlist_KeepsOnlyMatches( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 10, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ), "Independence Day" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Allowlist );

        // Only July 4 is allowed
        Assert.HasCount( 1, result.Occurrences );
        Assert.HasCount( 8, result.Suppressed );
    }

    [TestMethod]
    public void Allowlist_ThreeDates_KeepsThree( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 1, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 1, 15, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 1, 1 ), "New Year" ),
            MakeFullDayHoliday( new DateOnly( 2026, 1, 5 ), "Custom Holiday A" ),
            MakeFullDayHoliday( new DateOnly( 2026, 1, 10 ), "Custom Holiday B" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Allowlist );

        Assert.HasCount( 3, result.Occurrences );
    }

    // ── Full-Day Holiday Matching ──────────────────────────────────────────────

    [TestMethod]
    public void FullDay_ExactDateMatch( ) {
        DateTime utcOccurrence = new( 2026, 7, 4, 14, 0, 0, DateTimeKind.Utc );
        HolidayDate holiday = MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ) );
        Assert.IsTrue( ScheduleCalculator.IsOccurrenceOnHoliday( utcOccurrence, holiday ) );
    }

    [TestMethod]
    public void FullDay_DifferentDate_NoMatch( ) {
        DateTime utcOccurrence = new( 2026, 7, 5, 14, 0, 0, DateTimeKind.Utc );
        HolidayDate holiday = MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ) );
        Assert.IsFalse( ScheduleCalculator.IsOccurrenceOnHoliday( utcOccurrence, holiday ) );
    }

    // ── Time-Window Holiday Matching ───────────────────────────────────────────

    [TestMethod]
    public void TimeWindow_InsideWindow_Matches( ) {
        // Holiday: Jul 4 2026, 9:30 AM – 4:00 PM America/New_York
        HolidayDate holiday = MakeTimeWindowHoliday(
            new DateOnly( 2026, 7, 3 ),
            new TimeOnly( 9, 30 ), new TimeOnly( 16, 0 ), "America/New_York" );

        // 2:00 PM EDT = 18:00 UTC (EDT is UTC-4)
        DateTime utcOccurrence = new( 2026, 7, 3, 18, 0, 0, DateTimeKind.Utc );
        Assert.IsTrue( ScheduleCalculator.IsOccurrenceOnHoliday( utcOccurrence, holiday ) );
    }

    [TestMethod]
    public void TimeWindow_OutsideWindow_NoMatch( ) {
        // Holiday: Jul 3 2026, 9:30 AM – 4:00 PM America/New_York
        HolidayDate holiday = MakeTimeWindowHoliday(
            new DateOnly( 2026, 7, 3 ),
            new TimeOnly( 9, 30 ), new TimeOnly( 16, 0 ), "America/New_York" );

        // 6:00 PM EDT = 22:00 UTC — outside window
        DateTime utcOccurrence = new( 2026, 7, 3, 22, 0, 0, DateTimeKind.Utc );
        Assert.IsFalse( ScheduleCalculator.IsOccurrenceOnHoliday( utcOccurrence, holiday ) );
    }

    [TestMethod]
    public void TimeWindow_Blocklist_BlocksInsidePreservesOutside( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 3 ), new TimeOnly( 14, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 5, 0, 0, 0, DateTimeKind.Utc );

        // Holiday: Jul 3, 9:30 AM – 4:00 PM UTC
        List<HolidayDate> holidays = [
            MakeTimeWindowHoliday(
                new DateOnly( 2026, 7, 3 ),
                new TimeOnly( 9, 30 ), new TimeOnly( 16, 0 ), "UTC", "Market Closure" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        // Jul 3 at 14:00 UTC is within window → blocked; Jul 4 at 14:00 UTC is not Jul 3 → kept
        Assert.HasCount( 1, result.Occurrences );
        Assert.HasCount( 1, result.Suppressed );
        Assert.AreEqual( "Market Closure", result.Suppressed[0].HolidayName );
    }

    // ── Null / Empty Calendar Passthrough ──────────────────────────────────────

    [TestMethod]
    public void NullHolidayDates_KeepsAllOccurrences( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 5, 0, 0, 0, DateTimeKind.Utc );

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, null, HolidayCalendarMode.Blocklist );

        Assert.HasCount( 4, result.Occurrences );
        Assert.HasCount( 0, result.Suppressed );
    }

    [TestMethod]
    public void EmptyHolidayDates_KeepsAllOccurrences( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 5, 0, 0, 0, DateTimeKind.Utc );

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, [], HolidayCalendarMode.Blocklist );

        Assert.HasCount( 4, result.Occurrences );
        Assert.HasCount( 0, result.Suppressed );
    }

    [TestMethod]
    public void NullMode_KeepsAllOccurrences( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 5, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ) )];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, null );

        Assert.HasCount( 4, result.Occurrences );
        Assert.HasCount( 0, result.Suppressed );
    }

    // ── Suppressed Tracking ────────────────────────────────────────────────────

    [TestMethod]
    public void Suppressed_ContainsCorrectDetails( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 3 ), new TimeOnly( 12, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 6, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ), "Independence Day" ),
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        Assert.HasCount( 1, result.Suppressed );

        SuppressedOccurrence sup = result.Suppressed[0];
        Assert.AreEqual( "Independence Day", sup.HolidayName );
        Assert.Contains( "Independence Day", sup.Reason );
        Assert.AreEqual( new DateTime( 2026, 7, 4, 12, 0, 0, DateTimeKind.Utc ), sup.UtcTime );
    }

    [TestMethod]
    public void Allowlist_SuppressedReason_ContainsNotAllowed( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 7, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2026, 7, 3, 0, 0, 0, DateTimeKind.Utc );

        List<HolidayDate> holidays = [
            MakeFullDayHoliday( new DateOnly( 2026, 7, 4 ) ), // not in range
        ];

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Allowlist );

        Assert.HasCount( 0, result.Occurrences );
        Assert.IsNotEmpty( result.Suppressed );
        Assert.IsTrue( result.Suppressed.All( s => s.Reason.Contains( "allowed", StringComparison.OrdinalIgnoreCase ) ) );
    }

    // ── Holiday-Aware Full-Year Blocklist ──────────────────────────────────────

    [TestMethod]
    public void FullYear_DailySchedule_USFederal_CorrectOccurrences( ) {
        Schedule schedule = MakeDailySchedule( new DateOnly( 2026, 1, 1 ), new TimeOnly( 9, 0 ) );
        DateTime endOfWindow = new( 2027, 1, 1, 0, 0, 0, DateTimeKind.Utc );

        // Build 11 US Federal holidays for 2026
        ObservanceRule obs = ObservanceRule.SaturdayToFriday_SundayToMonday;
        HolidayCalendar cal = new( ) {
            Id = Guid.NewGuid( ),
            Name = "US Federal",
            Rules = [
                new HolidayRule { Name = "New Year", RuleType = HolidayRuleType.FixedDate, Month = 1, Day = 1, ObservanceRule = obs },
                new HolidayRule { Name = "MLK", RuleType = HolidayRuleType.NthWeekdayOfMonth, Month = 1, DayOfWeek = DayOfWeek.Monday, WeekNumber = 3 },
                new HolidayRule { Name = "PresDay", RuleType = HolidayRuleType.NthWeekdayOfMonth, Month = 2, DayOfWeek = DayOfWeek.Monday, WeekNumber = 3 },
                new HolidayRule { Name = "MemDay", RuleType = HolidayRuleType.LastWeekdayOfMonth, Month = 5, DayOfWeek = DayOfWeek.Monday },
                new HolidayRule { Name = "Juneteenth", RuleType = HolidayRuleType.FixedDate, Month = 6, Day = 19, ObservanceRule = obs },
                new HolidayRule { Name = "IndDay", RuleType = HolidayRuleType.FixedDate, Month = 7, Day = 4, ObservanceRule = obs },
                new HolidayRule { Name = "LaborDay", RuleType = HolidayRuleType.NthWeekdayOfMonth, Month = 9, DayOfWeek = DayOfWeek.Monday, WeekNumber = 1 },
                new HolidayRule { Name = "ColDay", RuleType = HolidayRuleType.NthWeekdayOfMonth, Month = 10, DayOfWeek = DayOfWeek.Monday, WeekNumber = 2 },
                new HolidayRule { Name = "VetDay", RuleType = HolidayRuleType.FixedDate, Month = 11, Day = 11, ObservanceRule = obs },
                new HolidayRule { Name = "Tgiving", RuleType = HolidayRuleType.NthWeekdayOfMonth, Month = 11, DayOfWeek = DayOfWeek.Thursday, WeekNumber = 4 },
                new HolidayRule { Name = "Xmas", RuleType = HolidayRuleType.FixedDate, Month = 12, Day = 25, ObservanceRule = obs },
            ],
        };

        IReadOnlyList<HolidayDate> holidays = HolidayCalculator.ComputeAllDatesForYear( cal, 2026 );

        ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
            schedule, endOfWindow, holidays, HolidayCalendarMode.Blocklist );

        // 365 days minus 11 holidays = 354
        Assert.HasCount( 354, result.Occurrences );
        Assert.HasCount( 11, result.Suppressed );
    }
}
