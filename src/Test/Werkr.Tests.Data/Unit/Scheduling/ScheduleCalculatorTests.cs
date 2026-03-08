using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Extensions;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Collections;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Contains unit tests for the <see cref="ScheduleCalculator"/> class, validating occurrence calculation across daily,
/// weekly, and monthly recurrence patterns with various time zones, repeat options, and expiration windows.
/// </summary>
[TestClass]
public class ScheduleCalculatorTests {

    #region Helpers

    /// <summary>
    /// Creates a <see cref="StartDateTimeInfo"/> instance from the specified date/time and time zone.
    /// </summary>
    private static StartDateTimeInfo MakeStart( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    /// <summary>
    /// Creates an <see cref="ExpirationDateTimeInfo"/> instance from the specified date/time and time zone.
    /// </summary>
    private static ExpirationDateTimeInfo MakeExpiration( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    /// <summary>
    /// Creates a minimal <see cref="DbSchedule"/> instance with a default test name for use in test fixtures.
    /// </summary>
    private static DbSchedule TestDb( ) => new( ) { Name = "Test" };

    #endregion Helpers

    #region Static Test Fields

    #region DateTime and TimeZone combinations

    /// <summary>
    /// Start date/time of 2020-01-01 12:00:00 UTC.
    /// </summary>
    internal static DateTime StartUTC = new(
        2020,
        1,
        1,
        12,
        0,
        0,
        DateTimeKind.Utc
    );
    /// <summary>
    /// UTC time zone.
    /// </summary>
    internal static TimeZoneInfo UtcTz = TimeZoneInfo.Utc;

    internal static DateTime StartLocal = new(
        2020,
        1,
        1,
        12,
        0,
        0,
        DateTimeKind.Local
    );
    /// <summary>
    /// Local system time zone.
    /// </summary>
    internal static TimeZoneInfo LocalTz = TimeZoneInfo.Local;

    internal static DateTime StartUnspecified = new(
        2020,
        1,
        1,
        12,
        0,
        0,
        DateTimeKind.Unspecified
    );
    /// <summary>
    /// Dateline Standard Time zone (UTC-12).
    /// </summary>
    internal static TimeZoneInfo DatelineTz = TimeZoneInfo.FindSystemTimeZoneById( "Dateline Standard Time" );

    /// <summary>
    /// Start date/time of 2020-01-01 12:00:00 at UTC+14:00 offset.
    /// </summary>
    internal static DateTime StartPlus14 = DateTimeOffset.Parse( "2020-01-01T12:00:00.0000000+14:00" ).DateTime;
    /// <summary>
    /// Line Islands Standard Time zone (UTC+14).
    /// </summary>
    internal static TimeZoneInfo LineIslandsTz = TimeZoneInfo.FindSystemTimeZoneById( "Line Islands Standard Time" );

    /// <summary>
    /// Start date/time of 2020-01-01 12:00:00 at UTC+13:00 offset.
    /// </summary>
    internal static DateTime StartPlus13 = DateTimeOffset.Parse( "2020-01-01T12:00:00.0000000+13:00" ).DateTime;
    /// <summary>
    /// Samoa Standard Time zone (UTC+13).
    /// </summary>
    internal static TimeZoneInfo SamoaTz = TimeZoneInfo.FindSystemTimeZoneById( "Samoa Standard Time" );

    /// <summary>
    /// Start date/time of 2020-01-01 12:00:00 at UTC+12:45 offset (Chatham Islands).
    /// </summary>
    internal static DateTime StartPlus1245 = DateTimeOffset.Parse( "2020-01-01T12:00:00.0000000+12:45" ).DateTime;
    /// <summary>
    /// Chatham Islands Standard Time zone (UTC+12:45).
    /// </summary>
    internal static TimeZoneInfo ChathamIslandsTz = TimeZoneInfo.FindSystemTimeZoneById( "Chatham Islands Standard Time" );

    /// <summary>
    /// Start date/time of 2020-01-01 12:00:00 at UTC-03:30 offset (Newfoundland).
    /// </summary>
    internal static DateTime StartMinus330 = DateTimeOffset.Parse( "2020-01-01T12:00:00.0000000-03:30" ).DateTime;
    /// <summary>
    /// Newfoundland Standard Time zone (UTC-03:30).
    /// </summary>
    internal static TimeZoneInfo NewfoundlandTz = TimeZoneInfo.FindSystemTimeZoneById( "Newfoundland Standard Time" );

    internal static DateTime EndOfWindow = new(
        2025,
        12,
        31,
        23,
        59,
        59,
        DateTimeKind.Utc
    );

    #endregion DateTime and TimeZone combinations

    #region RepeatOption

    internal static ScheduleRepeatOptions IntervalGreaterThanDuration = new() { RepeatIntervalMinutes = 5, RepeatDurationMinutes = 4 };
    internal static ScheduleRepeatOptions IntervalHalfDuration = new() { RepeatIntervalMinutes = 5, RepeatDurationMinutes = 10 };
    internal static ScheduleRepeatOptions IntervalMaxDurationMax = new() { RepeatIntervalMinutes = 1439, RepeatDurationMinutes = 1439 };
    internal static ScheduleRepeatOptions IntervalMinDurationMax = new() { RepeatIntervalMinutes = 1, RepeatDurationMinutes = 1439 };
    internal static ScheduleRepeatOptions IntervalHourlyDurationMax = new() { RepeatIntervalMinutes = 60, RepeatDurationMinutes = 1439 };
    internal static ScheduleRepeatOptions IntervalMinDurationMin = new() { RepeatIntervalMinutes = 1, RepeatDurationMinutes = -1 };
    internal static ScheduleRepeatOptions IntervalMinDurationZero = new() { RepeatIntervalMinutes = 1, RepeatDurationMinutes = 0 };
    internal static ScheduleRepeatOptions Interval15MinDurationTwoHours = new() { RepeatIntervalMinutes = 15, RepeatDurationMinutes = 120 };

    #endregion RepeatOption

    #region Expiration DateTimeInfo

    internal static ExpirationDateTimeInfo ExpirationBeforeStartUtc = MakeExpiration(
        StartUTC.AddMinutes( -1 ),
        UtcTz
    );
    internal static ExpirationDateTimeInfo ExpirationOneDayAfterStartLocal = MakeExpiration(
        StartLocal.AddDays( 1 ),
        LocalTz
    );
    internal static ExpirationDateTimeInfo ExpirationOneWeekAfterStartUnspecified = MakeExpiration(
        StartUnspecified.AddDays( 7 ),
        DatelineTz
    );
    /// <summary>
    /// Line Islands Standard Time zone (UTC+14).
    /// </summary>
    internal static ExpirationDateTimeInfo ExpirationOneMonthAfterStartPlus14 = MakeExpiration(
        StartPlus14.AddMonths( 1 ),
        LineIslandsTz
    );
    /// <summary>
    /// Samoa Standard Time zone (UTC+13).
    /// </summary>
    internal static ExpirationDateTimeInfo ExpirationSixMonthsAfterStartPlus13 = MakeExpiration(
        StartPlus13.AddMonths( 6 ),
        SamoaTz
    );
    /// <summary>
    /// Chatham Islands Standard Time zone (UTC+12:45).
    /// </summary>
    internal static ExpirationDateTimeInfo ExpirationOneYearAfterStartPlus1245 = MakeExpiration(
        StartPlus1245.AddYears( 1 ),
        ChathamIslandsTz
    );
    /// <summary>
    /// Newfoundland Standard Time zone (UTC-03:30).
    /// </summary>
    internal static ExpirationDateTimeInfo ExpirationTwoYearAfterStartMinus330 = MakeExpiration(
        StartMinus330.AddYears( 2 ),
        NewfoundlandTz
    );
    internal static ExpirationDateTimeInfo ExpirationAfterEndOfWindow = MakeExpiration(
        EndOfWindow.AddMinutes( 1 ),
        UtcTz
    );

    #endregion Expiration DateTimeInfo

    #region Daily Recurrence

    internal static DailyRecurrence NegativeDays = new() { DayInterval = -1 };
    internal static DailyRecurrence ZeroDays = new() { DayInterval = 0 };
    internal static DailyRecurrence EveryDay = new() { DayInterval = 1 };
    internal static DailyRecurrence EveryThreeDays = new() { DayInterval = 3 };
    internal static DailyRecurrence EverySevenDays = new() { DayInterval = 7 };
    internal static DailyRecurrence EveryEightDays = new() { DayInterval = 8 };
    internal static DailyRecurrence EveryFourteenDays = new() { DayInterval = 14 };
    internal static DailyRecurrence EveryThirtyDays = new() { DayInterval = 30 };

    #endregion Daily Recurrence

    #region Weekly Recurrence

    internal static WeeklyRecurrence NegativeWeeksEveryDay = new() { WeekInterval = -1, DaysOfWeek = (DaysOfWeek)127 };
    internal static WeeklyRecurrence ZeroWeeksMondays = new() { WeekInterval = 0, DaysOfWeek = DaysOfWeek.Monday };
    internal static WeeklyRecurrence EveryWeekEveryDay = new() { WeekInterval = 1, DaysOfWeek = (DaysOfWeek)127 };
    internal static WeeklyRecurrence EveryTwoWeeksMWFS = new() { WeekInterval = 2, DaysOfWeek = (DaysOfWeek)85 };
    internal static WeeklyRecurrence EveryThreeWeeksTuThSat = new() { WeekInterval = 3, DaysOfWeek = (DaysOfWeek)42 };
    internal static WeeklyRecurrence EveryFourWeeksFriSatSun = new() { WeekInterval = 4, DaysOfWeek = (DaysOfWeek)112 };
    internal static WeeklyRecurrence EverySixWeeksMTWThF = new() { WeekInterval = 6, DaysOfWeek = (DaysOfWeek)31 };
    internal static WeeklyRecurrence EveryEightWeeksOnWed = new() { WeekInterval = 8, DaysOfWeek = DaysOfWeek.Wednesday };
    internal static WeeklyRecurrence EveryHundredAndSevenWeeksOnFri = new() { WeekInterval = 107, DaysOfWeek = DaysOfWeek.Friday };

    #endregion Weekly Recurrence

    #region Monthly Recurrence

    #region DayNumbersWithinMonths

    internal static MonthlyRecurrence JanuaryDayNum1 = new() { MonthsOfYear = (MonthsOfYear)16, DayNumbers = [1] };
    internal static MonthlyRecurrence DecemberDayNum27 = new() { MonthsOfYear = (MonthsOfYear)32768, DayNumbers = [27] };
    internal static MonthlyRecurrence QuarterlyDayNum = new() { MonthsOfYear = (MonthsOfYear)9360, DayNumbers = [1, 3, 5, 8, 15, -8, -5, -3, -2, -1] };
    internal static MonthlyRecurrence QuarterlyDayNum2 = new() { MonthsOfYear = (MonthsOfYear)18720, DayNumbers = [2, 4, 6, 8, 10, 12, 14, -14, -12, -10, -8, -6, -4, -2] };
    internal static MonthlyRecurrence JanMaySepFirstDayNumFifteenthLast = new() { MonthsOfYear = (MonthsOfYear)4368, DayNumbers = [1, 15, -1] };
    internal static MonthlyRecurrence MarJulNovDayNum = new() { MonthsOfYear = (MonthsOfYear)17472, DayNumbers = [1, 4, 5, 8, 15, -1, -2, -5, -8] };
    internal static MonthlyRecurrence FirstHalfOfYearDayNumsOn5 = new() { MonthsOfYear = (MonthsOfYear)1008, DayNumbers = [5, 10, 15, 20, 25, 30] };
    internal static MonthlyRecurrence LastHalfOfYearDayNumsFirstPlusLastWeek = new() { MonthsOfYear = (MonthsOfYear)64512, DayNumbers = [1, -5, -4, -3, -2, -1] };
    internal static MonthlyRecurrence AllMonthsAllDaysDayNum = new() { MonthsOfYear = (MonthsOfYear)65520, DayNumbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31] };
    internal static MonthlyRecurrence AllMonthsAllDaysDayNumTwice = new() { MonthsOfYear = (MonthsOfYear)65520, DayNumbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, -1, -2, -3, -4, -5, -6, -7, -8, -9, -10, -11, -12, -13, -14, -15, -16, -17, -18, -19, -20, -21, -22, -23, -24, -25, -26, -27, -28, -29, -30, -31] };

    #endregion DayNumbersWithinMonths

    #region WeekNumberWithinMonth

    internal static MonthlyRecurrence JanuaryFirstMonday = new() { MonthsOfYear = (MonthsOfYear)16, WeekNumber = (WeekNumberWithinMonth)1, DaysOfWeek = (DaysOfWeek)1 };
    internal static MonthlyRecurrence DecemberThirdWednesday = new() { MonthsOfYear = (MonthsOfYear)32768, WeekNumber = (WeekNumberWithinMonth)4, DaysOfWeek = (DaysOfWeek)8 };
    internal static MonthlyRecurrence QuarterlyWeekNum = new() { MonthsOfYear = (MonthsOfYear)9360, WeekNumber = (WeekNumberWithinMonth)6, DaysOfWeek = (DaysOfWeek)31 };
    internal static MonthlyRecurrence QuarterlyWeekNum2 = new() { MonthsOfYear = (MonthsOfYear)18720, WeekNumber = (WeekNumberWithinMonth)5, DaysOfWeek = (DaysOfWeek)5 };
    internal static MonthlyRecurrence JanMaySepWeekNum = new() { MonthsOfYear = (MonthsOfYear)4368, WeekNumber = (WeekNumberWithinMonth)21, DaysOfWeek = (DaysOfWeek)21 };
    internal static MonthlyRecurrence MarJulNovWeekNum = new() { MonthsOfYear = (MonthsOfYear)17472, WeekNumber = (WeekNumberWithinMonth)42, DaysOfWeek = (DaysOfWeek)96 };
    internal static MonthlyRecurrence FirstHalfOfYearWeekNum = new() { MonthsOfYear = (MonthsOfYear)1008, WeekNumber = (WeekNumberWithinMonth)6, DaysOfWeek = (DaysOfWeek)2 };
    internal static MonthlyRecurrence LastHalfOfYearWeekNum = new() { MonthsOfYear = (MonthsOfYear)64512, WeekNumber = (WeekNumberWithinMonth)56, DaysOfWeek = (DaysOfWeek)85 };
    internal static MonthlyRecurrence AllMonthsAllDaysWeekNum = new() { MonthsOfYear = (MonthsOfYear)65520, WeekNumber = (WeekNumberWithinMonth)63, DaysOfWeek = (DaysOfWeek)127 };

    #endregion WeekNumberWithinMonth

    #endregion Monthly Recurrence

    #endregion Static Test Fields

    #region CalculateOccurrences OnlyStartDateTimeInfo

    /// <summary>
    /// Verifies that a schedule with a UTC start date/time and no recurrence returns exactly one occurrence at the
    /// start time.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartUTC,
            UtcTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with a local time zone start date/time and no recurrence returns exactly one
    /// occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartLocal,
            LocalTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with an unspecified-kind start date/time in the Dateline time zone returns exactly one
    /// occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartUnspecified,
            DatelineTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with a UTC+14 (Line Islands) start date/time returns exactly one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14Dt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartPlus14,
            LineIslandsTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with a UTC+13 (Samoa) start date/time returns exactly one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13Dt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartPlus13,
            SamoaTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with a UTC+12:45 (Chatham Islands) start date/time returns exactly one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245Dt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartPlus1245,
            ChathamIslandsTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule with a UTC-03:30 (Newfoundland) start date/time returns exactly one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330Dt_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            StartMinus330,
            NewfoundlandTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a schedule whose start time equals the end-of-window boundary returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EndOfWindow_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            EndOfWindow,
            UtcTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a schedule whose start time is after the end-of-window boundary returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_AfterEndOfWindow_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule = TestDb(),
            StartDateTime = MakeStart(
            EndOfWindow.AddDays( 1 ),
            UtcTz
        )
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    #endregion CalculateOccurrences OnlyStartDateTimeInfo

    #region CalculateOccurrences RepeatOptions

    /// <summary>
    /// Verifies that a UTC schedule with repeat interval greater than duration returns a single occurrence (no
    /// effective repeat).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtRepeatOptionsIntervalGtrThanDuration_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions = IntervalGreaterThanDuration
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with a 5-minute interval over 10-minute duration returns three occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDtRepeatOptionsIntervalHalfDuration_ReturnsThreeOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions = IntervalHalfDuration
        };
        DateTime lastRepeatTime = schedule.StartDateTime!.UtcTime.AddMinutes( IntervalHalfDuration.RepeatDurationMinutes );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            3,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            lastRepeatTime,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that maximum interval (1439 min) with maximum duration (1439 min) produces exactly two occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDtMaxRepeatOptionsIntervalMaxDurationMax_RepeatsOnce( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions = IntervalMaxDurationMax
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddMinutes( IntervalMaxDurationMax.RepeatIntervalMinutes ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule with 1-minute interval and max duration repeats once per minute for a full day
    /// (1440 occurrences).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DtRepeatOptionsMinIntervalMaxDuration_RepeatsOncePerMinuteForOneDay( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions = IntervalMinDurationMax
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1440,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule with hourly interval and max duration repeats once per hour for a full day (24
    /// occurrences).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DtRepeatOptionsIntervalHourlyMaxDuration_RepeatsOnceAnHourForOneDay( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions = IntervalHourlyDurationMax
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            24,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddHours( 23 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule with 1-minute interval and indefinite duration (-1) repeats every minute
    /// until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DtRepeatOptionsMinIntervalMinDuration_RepeatsUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions = IntervalMinDurationMin
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            3156585,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            EndOfWindow.AddSeconds( -59 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule with 1-minute interval and zero duration does not repeat (single occurrence).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DtRepeatOptionsMinIntervalZeroDuration_DoesNotRepeat( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions = IntervalMinDurationZero
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with 15-minute interval over 2-hour duration produces exactly 9 occurrences spaced
    /// 15 minutes apart.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtRepeatOptionsInterval15mDuration2h_Repeats9Times( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions = Interval15MinDurationTwoHours
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            9,
            occurrences.Count( )
        );
        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = occurrenceTime.AddMinutes( Interval15MinDurationTwoHours.RepeatIntervalMinutes );
        }
    }

    /// <summary>
    /// Verifies that a schedule starting at the end-of-window with repeat options returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EOWRepeatOptions_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            RepeatOptions = Interval15MinDurationTwoHours
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    #endregion CalculateOccurrences RepeatOptions

    #region CalculateOccurrences ExpirationDateTimeInfo

    /// <summary>
    /// Verifies that a UTC schedule with an expiration before the start time returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration    = ExpirationBeforeStartUtc
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule expiring one day after start returns exactly one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtExp1dAfter_ReturnsOneOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration    = ExpirationOneDayAfterStartLocal
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with repeat (interval > duration) and expiration before start returns no
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtRepeatOptionsIntervalGtrThanDurationExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions = IntervalGreaterThanDuration,
            Expiration    = ExpirationBeforeStartUtc
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with half-duration repeat and 1-day expiration returns three occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDTRepeatOptionsIntervalHalfDurationExp1dAfter_ReturnsThreeOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions = IntervalHalfDuration,
            Expiration    = ExpirationOneDayAfterStartLocal
        };
        DateTime lastRepeatTime = schedule.StartDateTime!.UtcTime.AddMinutes( IntervalHalfDuration.RepeatDurationMinutes );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            3,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            lastRepeatTime,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that an unspecified-kind schedule with max repeat options and 1-week expiration returns two
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDTRepeatOptionsMaxIntervalMaxDurationExp1wAfter_ReturnsTwoOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions = IntervalMaxDurationMax,
            Expiration    = ExpirationOneWeekAfterStartUnspecified
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddMinutes( IntervalMaxDurationMax.RepeatIntervalMinutes ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule with 1-minute interval, max duration, and 1-month expiration repeats 1440 times
    /// (one full day).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DTRepeatOptionsMinIntervalMaxDurationExp1MAfter_RepeatsEveryMinuteForOneDay( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions = IntervalMinDurationMax,
            Expiration    = ExpirationOneMonthAfterStartPlus14
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1440,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule with hourly interval, max duration, and 6-month expiration repeats 24 times per
    /// day.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DTRepeatOptionsIntervalHourlyMaxDurationExp6MAfter_RepeatsEveryHourForOneDay( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions = IntervalHourlyDurationMax,
            Expiration    = ExpirationSixMonthsAfterStartPlus13
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            24,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddHours( 23 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule with 1-minute interval, indefinite duration, and 1-year expiration repeats
    /// every minute for one year (527040 occurrences).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DTRepeatOptionsMinIntervalMinDurationExp1yAfter_RepeatsEveryMinuteForOneYear( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions = IntervalMinDurationMin,
            Expiration    = ExpirationOneYearAfterStartPlus1245
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            527040,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddYears( 1 ).AddMinutes( -1 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule with 1-minute interval, zero duration, and 2-year expiration returns a single
    /// occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DTRepeatOptionsMinIntervalZeroDurationExp2yAfter_ReturnsSingleOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions = IntervalMinDurationZero,
            Expiration    = ExpirationTwoYearAfterStartMinus330
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with 15-minute interval, 2-hour duration, and expiration after window returns 9
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcRepeatOptions15mInterval2hDurationExpAfterWindow_ReturnsNineOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions = Interval15MinDurationTwoHours,
            Expiration    = ExpirationAfterEndOfWindow
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            9,
            occurrences.Count( )
        );
        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = occurrenceTime.AddMinutes( Interval15MinDurationTwoHours.RepeatIntervalMinutes );
        }
    }

    /// <summary>
    /// Verifies that a schedule starting at end-of-window with repeat options and post-window expiration returns no
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EowUtcDtExpAfterEnd_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            RepeatOptions = Interval15MinDurationTwoHours,
            Expiration    = ExpirationAfterEndOfWindow
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    #endregion CalculateOccurrences ExpirationDateTimeInfo

    #region CalculateOccurrences DailyRecurrence

    /// <summary>
    /// Verifies that a UTC schedule with a negative day interval returns a single occurrence (invalid interval treated
    /// as no recurrence).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtNegativeDays_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            DailyRecurrence = NegativeDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with a zero day interval returns a single occurrence (invalid interval
    /// treated as no recurrence).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDtZeroDays_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartLocal,
                LocalTz
            ),
            DailyRecurrence = ZeroDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a Dateline-zone schedule recurring every day generates one occurrence per day until the
    /// end-of-window (2191 total).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDtEveryDay_ReturnsOneOccurrencePerDayUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            DailyRecurrence = EveryDay
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( EndOfWindow )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2191,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule recurring every 3 days generates 731 occurrences until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DtEveryThreeDays_ReturnsOneOccurrenceEveryThreeDaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            DailyRecurrence = EveryThreeDays
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2190 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            731,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule recurring every 7 days generates 314 occurrences until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DtEverySevenDays_ReturnsOneOccurrenceEverySevenDaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            DailyRecurrence = EverySevenDays
        };
        DateOnly endOfWindowDate = DateOnly.FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2191 ) );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            314,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        // Compare date only — UTC hour may differ from start due to DST transitions in Samoa timezone.
        Assert.AreEqual(
            endOfWindowDate,
            DateOnly.FromDateTime( occurrences.Last( ) )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule recurring every 8 days generates 275 occurrences until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DtEveryEightDays_ReturnsOneOccurrenceEveryEightDaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            DailyRecurrence = EveryEightDays
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2192 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            275,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule recurring every 14 days generates 157 occurrences until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DtEveryFourteenDays_ReturnsOneOccurrenceEveryFourteenDaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            DailyRecurrence = EveryFourteenDays
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2184 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            157,
            occurrences.LongCount( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule recurring every 30 days generates 74 occurrences until the end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DtEveryThirtyDays_ReturnsOneOccurrenceEveryThirtyDaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            DailyRecurrence = EveryThirtyDays
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2190 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            74,
            occurrences.LongCount( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a schedule at end-of-window with negative day interval returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EndOfWindowNegativeDays_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            DailyRecurrence = NegativeDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a schedule starting after end-of-window with daily recurrence returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_AfterEndOfWindowEveryDay_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                EndOfWindow.AddDays( 1 ),
                UtcTz
            ),
            DailyRecurrence = EveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule recurring every day generates exactly 2192 occurrences, each one day apart.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtEveryDay_Returns2192OccurrencesEachOneDayApart( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            DailyRecurrence = EveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2192,
            occurrences.Count( )
        );
        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = occurrenceTime.AddDays( EveryDay.DayInterval );
        }
    }

    /// <summary>
    /// Verifies that a UTC schedule with half-duration repeat and daily recurrence generates 6576 occurrences (3 per
    /// day).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcRepeatOptionsIntervalHalfDurationOccursEveryDay_Returns4383Occurrences( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions   = IntervalHalfDuration,
            DailyRecurrence = EveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            6576,
            occurrences.Count( )
        );
        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        int count = 0;
        foreach (DateTime occurrence in occurrences) {
            count++;
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = count % 3 == 0
                                ? occurrenceTime
                                    .AddMinutes( IntervalHalfDuration.RepeatIntervalMinutes * -2 )
                                    .AddDays( EveryDay.DayInterval )
                                : occurrenceTime
                                    .AddMinutes( IntervalHalfDuration.RepeatIntervalMinutes );
        }
    }

    /// <summary>
    /// Verifies that a UTC daily schedule with expiration before start returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtDrEveryDayExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration      = ExpirationBeforeStartUtc,
            DailyRecurrence = EveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule recurring every 3 days with 1-day expiration returns only one occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtDrEveryThreeDaysExp1dAfter_ReturnsOneOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration      = ExpirationOneDayAfterStartLocal,
            DailyRecurrence = EveryThreeDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC daily schedule with repeat (interval > duration) and expiration before start returns no
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtRepeatOptionsIntervalGtrThanDurationDrEveryDayExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions   = IntervalGreaterThanDuration,
            Expiration      = ExpirationBeforeStartUtc,
            DailyRecurrence = EveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with half-duration repeat, negative day interval, and 1-day expiration
    /// returns three occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDTRepeatOptionsIntervalHalfDurationDrNegativeDaysExp1dAfter_ReturnsThreeOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions   = IntervalHalfDuration,
            Expiration      = ExpirationOneDayAfterStartLocal,
            DailyRecurrence = NegativeDays
        };
        DateTime lastRepeatTime = schedule.StartDateTime!.UtcTime.AddMinutes( IntervalHalfDuration.RepeatDurationMinutes );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            3,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            lastRepeatTime,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a Dateline-zone schedule with max repeat, zero day interval, and 1-week expiration returns two
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDTRepeatOptionsMaxIntervalMaxDurationDrZeroDaysExp1wAfter_ReturnsTwoOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions   = IntervalMaxDurationMax,
            Expiration      = ExpirationOneWeekAfterStartUnspecified,
            DailyRecurrence = ZeroDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddMinutes( IntervalMaxDurationMax.RepeatIntervalMinutes ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule with 1-minute repeat, max duration, daily recurrence, and 1-month expiration
    /// generates 44640 occurrences over 31 days.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DTRepeatOptionsMinIntervalMaxDurationDrEveryDayExp1MAfter_RepeatsEveryMinuteForThirtyOneDays( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions   = IntervalMinDurationMax,
            Expiration      = ExpirationOneMonthAfterStartPlus14,
            DailyRecurrence = EveryDay
        };
        DateTime finalDt = DateOnly
                            .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 31 ) )
                            .ToDateTime(
                                TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                DateTimeKind.Utc
                            ).AddMinutes( -1 );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            44640,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            finalDt,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule with hourly repeat, max duration, every-3-day recurrence, and 6-month expiration
    /// generates 1464 hourly occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DTRepeatOptionsIntervalHourlyMaxDurationDrEveryThreeDaysExp6MAfter_ReturnsHourlyOccurrencesEveryThreeDaysForSixMonths( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions   = IntervalHourlyDurationMax,
            Expiration      = ExpirationSixMonthsAfterStartPlus13,
            DailyRecurrence = EveryThreeDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1464,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddDays( 181 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule with 1-minute repeat, indefinite duration, every-7-day recurrence, and 1-year
    /// expiration generates 527040 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DTRepeatOptionsMinIntervalMinDurationDrEverySevenDaysExp1yAfter_RepeatsEveryMinuteForOneYear( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions   = IntervalMinDurationMin,
            Expiration      = ExpirationOneYearAfterStartPlus1245,
            DailyRecurrence = EverySevenDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            527040,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddYears( 1 ).AddMinutes( -1 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule with 1-minute repeat, zero duration, every-8-day recurrence, and 2-year
    /// expiration generates 92 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DTRepeatOptionsMinIntervalZeroDurationDrEveryEightDaysExp2yAfter_ReoccursOnceEveryEightDaysForTwoYears( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions   = IntervalMinDurationZero,
            Expiration      = ExpirationTwoYearAfterStartMinus330,
            DailyRecurrence = EveryEightDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            92,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddDays( 728 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with 15-minute repeat, 2-hour duration, every-14-day recurrence, and post-window
    /// expiration generates 1413 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcRepeatOptions15mInterval2hDurationDrEveryFourteenDaysExpAfterWindow_RepeatsEveryFifteenMinutesForTwoHoursThenReoccursEvery14DaysUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions   = Interval15MinDurationTwoHours,
            Expiration      = ExpirationAfterEndOfWindow,
            DailyRecurrence = EveryFourteenDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1413,
            occurrences.Count( )
        );
        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        int count = 0;
        foreach (DateTime occurrence in occurrences) {
            count++;
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = count % 9 == 0
                                ? occurrenceTime.AddMinutes( Interval15MinDurationTwoHours.RepeatDurationMinutes * -1 ).AddDays( 14 )
                                : occurrenceTime.AddMinutes( Interval15MinDurationTwoHours.RepeatIntervalMinutes );
        }
    }

    /// <summary>
    /// Verifies that a schedule at end-of-window with repeat, post-window expiration, and 30-day recurrence returns no
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EowUtcDtDrEveryThirtyDaysExpAfterEnd_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            RepeatOptions   = Interval15MinDurationTwoHours,
            Expiration      = ExpirationAfterEndOfWindow,
            DailyRecurrence = EveryThirtyDays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    #endregion CalculateOccurrences DailyRecurrence

    #region CalculateOccurrences WeeklyRecurrence

    /// <summary>
    /// Verifies that a UTC schedule with a negative week interval and all days returns a single occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtWrNegativeWeeksEveryDay_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            WeeklyRecurrence = NegativeWeeksEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with a zero week interval and Mondays returns a single occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDtWrZeroWeeksMondays_ReturnsSingleOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartLocal,
                LocalTz
            ),
            WeeklyRecurrence = ZeroWeeksMondays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
    }

    /// <summary>
    /// Verifies that a Dateline-zone schedule recurring every week on all days generates 2191 occurrences until
    /// end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDtWrEveryWeekEveryDay_ReturnsOneOccurrencePerDayUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            WeeklyRecurrence = EveryWeekEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            2191
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule recurring every 2 weeks on Mon/Wed/Fri/Sun generates 627 occurrences until
    /// end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DtWrEveryTwoWeeksMWFS_ReturnsOneOccurrenceEveryMWFSOnTwoWeekIntervals( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            WeeklyRecurrence = EveryTwoWeeksMWFS
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            627
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule recurring every 3 weeks on Tue/Thu/Sat generates 315 occurrences until
    /// end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DtWrEveryThreeWeeksTuThSat_ReturnsOneOccurrenceEveryThirdWeekOnTuThSatUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            WeeklyRecurrence = EveryThreeWeeksTuThSat
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            315
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule recurring every 4 weeks on Fri/Sat/Sun generates 238 occurrences until
    /// end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DtWrEveryFourWeeksFriSatSun_ReturnsOneOccurrenceEveryFourWeeksOnFriSatSunUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            WeeklyRecurrence = EveryFourWeeksFriSatSun
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2188 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            238
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule recurring every 6 weeks on weekdays (Mon-Fri) generates 263 occurrences with
    /// 3 in the first partial week.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DtWrEverySixWeeksMTWThF_ReturnsThreeOccurrenceInFirstWeekAndFiveOccurrencesEverySixWeeksThereAfterUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            WeeklyRecurrence = EverySixWeeksMTWThF
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2186 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            263,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            263
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule recurring every 8 weeks on Wednesday generates 40 occurrences until
    /// end-of-window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DtWrEveryEightWeeksWednesday_ReturnsOneOccurrenceEvery8WeeksUntilEndOfWindow( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            WeeklyRecurrence = EveryEightWeeksOnWed
        };
        DateTime endOfWindowDate = DateOnly
                                    .FromDateTime( schedule.StartDateTime!.UtcTime.AddDays( 2184 ) )
                                    .ToDateTime(
                                        TimeOnly.FromDateTime( schedule.StartDateTime!.UtcTime ),
                                        DateTimeKind.Utc
                                    );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            40,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            endOfWindowDate,
            occurrences.Last( )
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            40
        );
    }

    /// <summary>
    /// Verifies that a schedule at end-of-window with negative weeks and all days returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EndOfWindowWrNegativeWeeksEveryDay_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            WeeklyRecurrence = NegativeWeeksEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a schedule after end-of-window with zero weeks on Mondays returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_AfterEndOfWindowWrZeroWeeksMondays_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                EndOfWindow.AddDays( 1 ),
                UtcTz
            ),
            WeeklyRecurrence = ZeroWeeksMondays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule recurring every week on all days generates 2192 occurrences, each one day apart.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtWrEveryWeekEveryDay_Returns2192OccurrencesEachOneDayApart( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            WeeklyRecurrence = EveryWeekEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            2192
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with half-duration repeat and biweekly Mon/Wed/Fri/Sun recurrence repeats 3 times
    /// each occurrence day.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcRepeatOptionsIntervalHalfDurationWrEveryTwoWeeksMWFS_RepeatsThreeTimesPerMonWedFriSunEveryTwoWeeks( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions    = IntervalHalfDuration,
            WeeklyRecurrence = EveryTwoWeeksMWFS
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );

        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        int count = 4;
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = count % 3 == 0
                ? occurrenceTime
                                    .AddMinutes( schedule.RepeatOptions!.RepeatIntervalMinutes * -2 )
                                    .AddDays( count % 12 == 0 ? 8 : 2 )
                : occurrenceTime.AddMinutes( schedule.RepeatOptions!.RepeatIntervalMinutes );
            count++;
        }
    }

    /// <summary>
    /// Verifies that a UTC schedule with every-3-week Tue/Thu/Sat recurrence and pre-start expiration returns no
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtWrEveryThreeWeeksTuThSatExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration       = ExpirationBeforeStartUtc,
            WeeklyRecurrence = EveryThreeWeeksTuThSat
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with every-4-week Fri/Sat/Sun recurrence and 1-day expiration returns one
    /// occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtWrEveryFourWeeksFriSatSunExp1dAfter_ReturnsOneOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration       = ExpirationOneDayAfterStartLocal,
            WeeklyRecurrence = EveryFourWeeksFriSatSun
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with repeat (interval > duration), every-6-week weekday recurrence, and pre-start
    /// expiration returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcDtRepeatOptionsIntervalGtrThanDurationWrEverySixWeeksMTWThFExpBeforeStart_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions    = IntervalGreaterThanDuration,
            Expiration       = ExpirationBeforeStartUtc,
            WeeklyRecurrence = EverySixWeeksMTWThF
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a local-time schedule with half-duration repeat, every-8-week Wednesday recurrence, and 1-day
    /// expiration returns 3 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_LocalDTRepeatOptionsIntervalHalfDurationWrEveryEightWeeksWednesdayExp1dAfter_ReturnsThreeOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions    = IntervalHalfDuration,
            Expiration       = ExpirationOneDayAfterStartLocal,
            WeeklyRecurrence = EveryEightWeeksOnWed
        };
        DateTime lastRepeatTime = schedule.StartDateTime!.UtcTime.AddMinutes( IntervalHalfDuration.RepeatDurationMinutes );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            3,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            lastRepeatTime,
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a Dateline-zone schedule with max repeat, negative weeks all days, and 1-week expiration returns 2
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UnspecDTRepeatOptionsMaxIntervalMaxDurationWrNegativeWeeksEveryDayExp1wAfter_ReturnsTwoOccurrences( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions    = IntervalMaxDurationMax,
            Expiration       = ExpirationOneWeekAfterStartUnspecified,
            WeeklyRecurrence = NegativeWeeksEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            2,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddMinutes( IntervalMaxDurationMax.RepeatIntervalMinutes ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+14 schedule with 1-minute repeat, max duration, zero-week Mondays, and 1-month expiration
    /// generates 1440 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P14DTRepeatOptionsMinIntervalMaxDurationWrZeroWeeksMondaysExp1MAfter_RepeatsEveryMinuteForOneDay( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions    = IntervalMinDurationMax,
            Expiration       = ExpirationOneMonthAfterStartPlus14,
            WeeklyRecurrence = ZeroWeeksMondays
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1440,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddMinutes( 1439 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+13 schedule with hourly repeat, max duration, every-week-all-days recurrence, and 6-month
    /// expiration generates 4368 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P13DTRepeatOptionsIntervalHourlyMaxDurationWrEveryWeekEveryDayExp6MAfter_ReturnsHourlyOccurrencesEveryDaysForSixMonths( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions    = IntervalHourlyDurationMax,
            Expiration       = ExpirationSixMonthsAfterStartPlus13,
            WeeklyRecurrence = EveryWeekEveryDay
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            4368,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddDays( 182 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule with 1-minute repeat, indefinite duration, biweekly MWFS recurrence, and
    /// 1-year expiration repeats every minute for one year.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DTRepeatOptionsMinIntervalMinDurationWrEveryTwoWeeksMWFSExp1yAfter_RepeatsEveryMinuteForOneYear( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions    = IntervalMinDurationMin,
            Expiration       = ExpirationOneYearAfterStartPlus1245,
            WeeklyRecurrence = EveryTwoWeeksMWFS
        };
        TimeSpan totalScheduleTime = schedule.Expiration!.UtcTime - schedule.StartDateTime!.UtcTime;
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            totalScheduleTime.TotalMinutes,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddYears( 1 ).AddMinutes( -1 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC+12:45 schedule with 1-minute repeat, max duration, every-week-all-days recurrence, and
    /// 1-year expiration generates occurrences totaling the schedule span minus 60.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_P1245DTRepeatOptionsMinIntervalMaxDurationWrEveryWeekEveryDayExp1yAfter_RepeatsEveryMinuteForOneYear( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions    = IntervalMinDurationMax,
            Expiration       = ExpirationOneYearAfterStartPlus1245,
            WeeklyRecurrence = EveryWeekEveryDay
        };
        TimeSpan totalScheduleTime = schedule.Expiration!.UtcTime - schedule.StartDateTime!.UtcTime;
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        // DST offset change causes a 60-minute gap
        Assert.AreEqual(
            totalScheduleTime.TotalMinutes - 60,
            occurrences.Count( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences.First( )
        );
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime.AddYears( 1 ).AddMinutes( -1 ),
            occurrences.Last( )
        );
    }

    /// <summary>
    /// Verifies that a UTC-03:30 schedule with zero-duration repeat, every-3-week Tue/Thu/Sat recurrence, and 2-year
    /// expiration generates 105 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_M330DTRepeatOptionsMinIntervalZeroDurationWrEveryThreeWeeksTuThSatExp2yAfter_RepeatsThreeDaysAWeekEveryThreeWeeksForTwoYears( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions    = IntervalMinDurationZero,
            Expiration       = ExpirationTwoYearAfterStartMinus330,
            WeeklyRecurrence = EveryThreeWeeksTuThSat
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        CalculateOccurrences_SimpleWeeklyRecurrence(
            schedule,
            EndOfWindow,
            occurrences,
            105
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with 15-minute repeat, 2-hour duration, every-4-week Fri/Sat/Sun recurrence, and
    /// post-window expiration correctly interleaves repeat and recurrence occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_UtcRepeatOptions15mInterval2hDurationWrEveryFourWeeksFriSatSunExpAfterWindow_RepeatsEveryFifteenMinutesForTwoHoursAndScheduleReoccursEveryFourWeeksOnFriSatSun( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions    = Interval15MinDurationTwoHours,
            Expiration       = ExpirationAfterEndOfWindow,
            WeeklyRecurrence = EveryFourWeeksFriSatSun
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );

        DateTime occurrenceTime = schedule.StartDateTime!.UtcTime;
        int count = -8;
        Assert.AreEqual(
            occurrenceTime,
            occurrences.First( )
        );
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                occurrenceTime,
                occurrence
            );
            occurrenceTime = count == 0
                ? occurrenceTime
                                    .AddMinutes( schedule.RepeatOptions!.RepeatIntervalMinutes * -8 )
                                    .AddDays( 2 )
                : count % 9 == 0
                    ? occurrenceTime
                                    .AddMinutes( schedule.RepeatOptions!.RepeatIntervalMinutes * -8 )
                                    .AddDays( count % 27 == 0 ? 26 : 1 )
                    : occurrenceTime.AddMinutes( schedule.RepeatOptions!.RepeatIntervalMinutes );
            count++;
        }
    }

    /// <summary>
    /// Verifies that a schedule at end-of-window with repeat, post-window expiration, and 6-week weekday recurrence
    /// returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EowUtcDtWrEverySixWeeksMTWThFExpAfterEnd_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            RepeatOptions    = Interval15MinDurationTwoHours,
            Expiration       = ExpirationAfterEndOfWindow,
            WeeklyRecurrence = EverySixWeeksMTWThF
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a UTC schedule with 15-minute repeat, 2-hour duration, every-107-week Friday recurrence, and
    /// post-window expiration generates 36 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_EowUtcDtWrEveryHundredSevenWeeksOnFriExpAfterEnd( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions    = Interval15MinDurationTwoHours,
            Expiration       = ExpirationAfterEndOfWindow,
            WeeklyRecurrence = EveryHundredAndSevenWeeksOnFri
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            36,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Validates simple weekly recurrence results against a brute-force calculation.
    /// Only works for schedules without repeat options (except zero-duration).
    /// </summary>
    public static void CalculateOccurrences_SimpleWeeklyRecurrence(
        Schedule schedule,
        DateTime endOfWindow,
        IReadOnlyList<DateTime> occurrences,
        int validationCount
    ) {
        if (schedule?.WeeklyRecurrence == null) {
            throw new ArgumentNullException(
                nameof( schedule ),
                "Schedule must contain a weekly recurrence schedule."
            );
        } else if (endOfWindow.Kind != DateTimeKind.Utc) {
            throw new ArgumentException(
                "End of window must be in UTC.",
                nameof( endOfWindow )
            );
        }

        if (schedule.Expiration?.UtcTime != null && schedule.Expiration.UtcTime < endOfWindow) {
            endOfWindow = schedule.Expiration.UtcTime;
        }

        TimeSpan totalScheduleTime = endOfWindow - schedule.StartDateTime!.UtcTime;
        DateTime occurrence = schedule.StartDateTime!.TzTime;
        List<DateTime> calculatedOccurrences = [schedule.StartDateTime!.UtcTime];
        List<DayOfWeek> dayOfWeeks = schedule.WeeklyRecurrence.DaysOfWeek.GetDaysOfWeek();
        LoopingList<DayOfWeek> loopingWeek = [.. CalendarEnumExtensions.GetWeekOfDays()];

        DayOfWeek targetDay = occurrence.DayOfWeek;
        DayOfWeek startDay = loopingWeek[0];
        int firstWeekEnds = ScheduleCalculator.CalculateWeeklyOccurrences_GetDayDifference(
            loopingWeek,
            targetDay,
            startDay
        );

        int weekNum = 0;
        int weekDayCount = 0;
        for (int i = 0; i < totalScheduleTime.Days; i++) {
            occurrence = occurrence.AddDays( 1 );

            if (schedule.Expiration?.UtcTime != null && occurrence > schedule.Expiration.UtcTime) {
                break;
            }
            if (dayOfWeeks.Contains( occurrence.DayOfWeek ) && weekNum % schedule.WeeklyRecurrence.WeekInterval == 0) {
                calculatedOccurrences.Add( schedule.StartDateTime!.ConvertToUtc( occurrence ) );
            }

            if ((weekDayCount == 0 && i == firstWeekEnds) || (weekDayCount > 0 && weekDayCount % 7 == 0)) {
                weekDayCount++;
                weekNum++;
            } else if (weekDayCount > 0) {
                weekDayCount++;
            }
        }

        Assert.HasCount(
            validationCount,
            calculatedOccurrences,
            "validationCount must match the number of calculatedOccurrences."
        );
        Assert.AreEqual(
            validationCount,
            occurrences.Count( ),
            "SimpleWeeklyRecurrence calculatedOccurrences must match the number of the input occurrences."
        );
        for (int i = 0; i < calculatedOccurrences.Count; i++) {
            Assert.AreEqual(
                calculatedOccurrences[i],
                occurrences.ElementAt( i ),
                "SimpleWeeklyRecurrence calculatedOccurrences must also match the datetime of the input occurrences."
            );
        }
    }

    #endregion CalculateOccurrences WeeklyRecurrence

    #region MonthlyRecurrence

    /// <summary>
    /// Verifies that a monthly recurrence schedule starting at end-of-window returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceExpAfterEnd_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                EndOfWindow,
                UtcTz
            ),
            MonthlyRecurrence = JanMaySepFirstDayNumFifteenthLast
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence on January 1st returns 6 occurrences (2020-2025), each on January 1st.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceJanuaryDayNum1_ReturnsSixOccurrancesOnJanuaryFirst( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            MonthlyRecurrence = JanuaryDayNum1
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            6,
            occurrences.Count( )
        );
        int year = 2020;
        foreach (DateTime occurrence in occurrences) {
            Assert.AreEqual(
                new DateTime(
                    year,
                    1,
                    1,
                    StartUTC.Hour,
                    StartUTC.Minute,
                    StartUTC.Second,
                    DateTimeKind.Utc
                ),
                occurrence
            );
            year++;
        }
    }

    /// <summary>
    /// Verifies that a monthly recurrence on December 27th returns 6 December 27th occurrences plus the original start
    /// occurrence (7 total).
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceDecemberDayNum27_ReturnsSixDecember27thsPlusOriginalOccurrence( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            MonthlyRecurrence = DecemberDayNum27
        };
        DateTime dateTime = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(
                2020,
                12,
                27,
                StartLocal.Hour,
                StartLocal.Minute,
                StartLocal.Second,
                DateTimeKind.Local
            ),
            LocalTz
        );
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            7,
            occurrences.Count( )
        );
        int year = 2020;
        int count = 0;
        foreach (DateTime occurrence in occurrences) {
            if (count == 0) {
                Assert.AreEqual(
                    schedule.StartDateTime!.UtcTime,
                    occurrence
                );
                count++;
            } else {
                Assert.AreEqual(
                    new DateTime(
                        year,
                        12,
                        dateTime.Day,
                        dateTime.Hour,
                        dateTime.Minute,
                        dateTime.Second,
                        DateTimeKind.Utc
                    ),
                    occurrence
                );
                year++;
            }
        }
    }

    /// <summary>
    /// Verifies that a quarterly recurrence with 10 day numbers (positive and negative) generates 240 occurrences
    /// across Jan/Apr/Jul/Oct for 6 years.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceQuarterlyDayNum_Returns10OccurrencesInJanAprJulOctFor6Years( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            MonthlyRecurrence = QuarterlyDayNum
        };
        DateTime[] occurrences = [.. ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        )];
        DateTime startTime = schedule.StartDateTime!.TzTime;

        int[] thirtyOneDayMonths = [1, 3, 5, 8, 15, 24, 27, 29, 30, 31];
        int[] april = [1, 3, 5, 8, 15, 23, 26, 28, 29, 30];
        int count = 1;
        int year = 2020;
        for (int i = 0; i < 4 * 10 * 6; i++) {
            if (i % 10 == 0 && i != 0) {
                if (count == 4) {
                    count = 1;
                } else {
                    count++;
                }
            }
            int[] intArray = count == 2 ? april : thirtyOneDayMonths;
            int iterator = i % 10;
            int monthOfYear = count == 1 ? 1 : count == 2 ? 4 : count == 3 ? 7 : 10;

            DateTime scheduledTime = TimeZoneInfo.ConvertTimeToUtc(
                new(
                    year,
                    monthOfYear,
                    intArray[iterator],
                    startTime.Hour,
                    startTime.Minute,
                    startTime.Second,
                    DateTimeKind.Unspecified
                ),
                DatelineTz
            );
            Assert.AreEqual(
                scheduledTime,
                occurrences[i]
            );

            if ((i + 1) % 40 == 0) { year++; }
        }
        Assert.HasCount(
            240,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly recurrence with 14 day numbers generates 337 occurrences across Feb/May/Aug/Nov for 6
    /// years, accounting for leap years.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceQuarterlyDayNum2_Returns14OccurrencesInFebMayAugNovFor6Years( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            MonthlyRecurrence = QuarterlyDayNum2
        };
        DateTime[] occurrences = [.. ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        )];
        DateTime startTime = schedule.StartDateTime!.TzTime;

        int[] february = [2, 4, 6, 8, 10, 12, 14, 15, 17, 19, 21, 23, 25, 27];
        int[] leapFebruary = [2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28];
        int[] thirtyOneDayMonths = [2, 4, 6, 8, 10, 12, 14, 18, 20, 22, 24, 26, 28, 30];
        int[] november = [2, 4, 6, 8, 10, 12, 14, 17, 19, 21, 23, 25, 27, 29];
        int count = 1;
        int year = 2020;
        Assert.AreEqual(
            schedule.StartDateTime!.UtcTime,
            occurrences[0]
        );
        for (int i = 0; i < 4 * 14 * 6; i++) {
            if (i % 14 == 0 && i != 0) {
                if (count == 4) {
                    count = 1;
                } else {
                    count++;
                }
            }
            int[] intArray = count == 1
                            ? i is <= 14 or (>= 224 and < 280)                                ? leapFebruary
                                : february
                            : count == 4
                                ? november
                                : thirtyOneDayMonths;
            int iterator = i % 14;
            int monthOfYear = count == 1 ? 2 : count == 2 ? 5 : count == 3 ? 8 : 11;
            DateTime tzTime = new(
                year,
                monthOfYear,
                intArray[iterator],
                startTime.Hour,
                startTime.Minute,
                startTime.Second,
                DateTimeKind.Unspecified
            );
            DateTime scheduledTime = TimeZoneInfo.ConvertTimeToUtc(
                tzTime,
                LineIslandsTz
            );
            Assert.AreEqual(
                scheduledTime,
                occurrences[i + 1]
            );

            if ((i + 1) % 56 == 0) { year++; }
        }
        Assert.HasCount(
            337,
            occurrences
        );
    }

    // --- Monthly stub tests (values need to be calculated when algorithm runs) ---

    /// <summary>
    /// Verifies that a monthly recurrence on the 1st, 15th, and last day of Jan/May/Sep with a 6-month expiration
    /// returns 6 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceJanMaySepFirstDayNumFifteenthLast_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            Expiration        = ExpirationSixMonthsAfterStartPlus13,
            MonthlyRecurrence = JanMaySepFirstDayNumFifteenthLast
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            6,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence in Mar/Jul/Nov on 9 day numbers with a 1-year expiration returns 28
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceMarJulNovDayNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            Expiration        = ExpirationOneYearAfterStartPlus1245,
            MonthlyRecurrence = MarJulNovDayNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            28,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence for the first half of the year on every-5th-day numbers with zero-duration
    /// repeat returns 211 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceFirstHalfOfYearDayNumsOn5_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions     = IntervalMinDurationZero,
            MonthlyRecurrence = FirstHalfOfYearDayNumsOn5
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            211,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence for the last half of the year on the first and last 5 days with post-window
    /// expiration returns 217 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceLastHalfOfYearDayNumsFirstPlusLastWeek_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            Expiration        = ExpirationAfterEndOfWindow,
            MonthlyRecurrence = LastHalfOfYearDayNumsFirstPlusLastWeek
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            217,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence in all months on all day numbers (1-31) with interval-greater-than-duration
    /// repeat returns 2192 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceAllMonthsAllDaysDayNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions     = IntervalGreaterThanDuration,
            MonthlyRecurrence = AllMonthsAllDaysDayNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            2192,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence with all positive and negative day numbers (1-31 and -1 to -31) and 1-day
    /// expiration returns 1 occurrence.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceAllMonthsAllDaysDayNumTwice_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            Expiration        = ExpirationOneDayAfterStartLocal,
            MonthlyRecurrence = AllMonthsAllDaysDayNumTwice
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            1,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence on the first Monday of January returns 2 occurrences across the schedule
    /// window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceJanuaryFirstMonday_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            MonthlyRecurrence = JanuaryFirstMonday
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            2,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence on the third Wednesday of December returns 7 occurrences across the schedule
    /// window.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceDecemberThirdWednesday_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            MonthlyRecurrence = DecemberThirdWednesday
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            7,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly recurrence by week number (last week, weekdays) returns 241 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceQuarterlyWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            MonthlyRecurrence = QuarterlyWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            241,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly recurrence by week number (week 5, Mon/Wed) returns 63 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceQuarterlyWeekNum2_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            MonthlyRecurrence = QuarterlyWeekNum2
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            63,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence in Jan/May/Sep across multiple week numbers on Mon/Wed/Fri returns 113
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceJanMaySepWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            MonthlyRecurrence = JanMaySepWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            113,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence in Mar/Jul/Nov by week number on Sat/Sun with post-window expiration returns
    /// 73 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceMarJulNovWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            Expiration        = ExpirationAfterEndOfWindow,
            MonthlyRecurrence = MarJulNovWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            73,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence for the first half of the year (last week, Tuesday) with
    /// interval-greater-than-duration repeat returns 72 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceFirstHalfOfYearWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions     = IntervalGreaterThanDuration,
            MonthlyRecurrence = FirstHalfOfYearWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            72,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence for the last half of the year (weeks 4-6, Mon/Wed/Fri/Sun) with 1-day
    /// expiration returns 3 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceLastHalfOfYearWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            Expiration        = ExpirationOneDayAfterStartLocal,
            MonthlyRecurrence = LastHalfOfYearWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            3,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence in all months, all week numbers, all days with max repeat returns 4011
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_MonthlyRecurrenceAllMonthsAllDaysWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions     = IntervalMaxDurationMax,
            MonthlyRecurrence = AllMonthsAllDaysWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            4011,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence on January day 1 with interval-greater-than-duration repeat and pre-start
    /// expiration returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_ExpirationBeforeStartUtcIntervalGreaterThanDurationMrJanuaryDayNum1_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions     = IntervalGreaterThanDuration,
            Expiration        = ExpirationBeforeStartUtc,
            MonthlyRecurrence = JanuaryDayNum1
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence on December day 27 with half-duration repeat and 1-day expiration returns 3
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalHalfDurationExpirationOneDayAfterStartLocalMrDecemberDayNum27_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions     = IntervalHalfDuration,
            Expiration        = ExpirationOneDayAfterStartLocal,
            MonthlyRecurrence = DecemberDayNum27
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            3,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly day-number recurrence with max repeat and 1-week expiration returns 6 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMaxDurationMaxExpirationOneWeekAfterStartUnspecifiedMrQuarterlyDayNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions     = IntervalMaxDurationMax,
            Expiration        = ExpirationOneWeekAfterStartUnspecified,
            MonthlyRecurrence = QuarterlyDayNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            6,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly day-number recurrence (type 2) with 1-minute repeat, max duration, and 1-month
    /// expiration returns 1440 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationMaxExpirationOneMonthAfterStartPlus14MrQuarterlyDayNum2_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions     = IntervalMinDurationMax,
            Expiration        = ExpirationOneMonthAfterStartPlus14,
            MonthlyRecurrence = QuarterlyDayNum2
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            1440,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a Jan/May/Sep recurrence on 1st, 15th, and last with hourly repeat, max duration, and 6-month
    /// expiration returns 144 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalHourlyDurationMaxExpirationSixMonthsAfterStartPlus13MrJanMaySepFirstDayNumFifteenthLast_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions     = IntervalHourlyDurationMax,
            Expiration        = ExpirationSixMonthsAfterStartPlus13,
            MonthlyRecurrence = JanMaySepFirstDayNumFifteenthLast
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            144,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a Mar/Jul/Nov day-number recurrence with 1-minute repeat, indefinite duration, and 1-year
    /// expiration generates 527040 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationMinExpirationOneYearAfterStartPlus1245MrMarJulNovDayNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions     = IntervalMinDurationMin,
            Expiration        = ExpirationOneYearAfterStartPlus1245,
            MonthlyRecurrence = MarJulNovDayNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            527040,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a first-half-of-year every-5th-day-number recurrence with zero-duration repeat and 2-year
    /// expiration returns 71 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationZeroExpirationTwoYearAfterStartMinus330MrFirstHalfOfYearDayNumsOn5_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions     = IntervalMinDurationZero,
            Expiration        = ExpirationTwoYearAfterStartMinus330,
            MonthlyRecurrence = FirstHalfOfYearDayNumsOn5
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            71,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a last-half-of-year recurrence on first and last 5 days with 15-minute repeat, 2-hour duration,
    /// and post-window expiration returns 1953 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_ExpirationAfterEndOfWindowInterval15MinDurationTwoHoursMrLastHalfOfYearDayNumsFirstPlusLastWeek_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions     = Interval15MinDurationTwoHours,
            Expiration        = ExpirationAfterEndOfWindow,
            MonthlyRecurrence = LastHalfOfYearDayNumsFirstPlusLastWeek
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            1953,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that an all-months all-days day-number recurrence with interval-greater-than-duration repeat and
    /// pre-start expiration returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_ExpirationBeforeStartUtcIntervalGreaterThanDurationMrAllMonthsAllDaysDayNum_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions     = IntervalGreaterThanDuration,
            Expiration        = ExpirationBeforeStartUtc,
            MonthlyRecurrence = AllMonthsAllDaysDayNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that an all-months all-days (positive and negative) recurrence with half-duration repeat and 1-day
    /// expiration returns 3 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalHalfDurationExpirationOneDayAfterStartLocalMrAllMonthsAllDaysDayNumTwice_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions     = IntervalHalfDuration,
            Expiration        = ExpirationOneDayAfterStartLocal,
            MonthlyRecurrence = AllMonthsAllDaysDayNumTwice
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            3,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a January first-Monday week-number recurrence with max repeat and 1-week expiration returns 2
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMaxDurationMaxExpirationOneWeekAfterStartUnspecifiedMrJanuaryFirstMonday_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions     = IntervalMaxDurationMax,
            Expiration        = ExpirationOneWeekAfterStartUnspecified,
            MonthlyRecurrence = JanuaryFirstMonday
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            2,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a December third-Wednesday week-number recurrence with 1-minute repeat, max duration, and 1-month
    /// expiration returns 1440 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationMaxExpirationOneMonthAfterStartPlus14MrDecemberThirdWednesday_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions     = IntervalMinDurationMax,
            Expiration        = ExpirationOneMonthAfterStartPlus14,
            MonthlyRecurrence = DecemberThirdWednesday
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            1440,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly week-number recurrence with hourly repeat, max duration, and 6-month expiration
    /// returns 504 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalHourlyDurationMaxExpirationSixMonthsAfterStartPlus13MrQuarterlyWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus1245,
                ChathamIslandsTz
            ),
            RepeatOptions     = IntervalHourlyDurationMax,
            Expiration        = ExpirationSixMonthsAfterStartPlus13,
            MonthlyRecurrence = QuarterlyWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            504,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a quarterly week-number recurrence (type 2) with 1-minute repeat, indefinite duration, and 1-year
    /// expiration returns 526005 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationMinExpirationOneYearAfterStartPlus1245MrQuarterlyWeekNum2_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartMinus330,
                NewfoundlandTz
            ),
            RepeatOptions     = IntervalMinDurationMin,
            Expiration        = ExpirationOneYearAfterStartPlus1245,
            MonthlyRecurrence = QuarterlyWeekNum2
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            526005,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a Jan/May/Sep week-number recurrence with zero-duration repeat and 2-year expiration returns 37
    /// occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMinDurationZeroExpirationTwoYearAfterStartMinus330MrJanMaySepWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUTC,
                UtcTz
            ),
            RepeatOptions     = IntervalMinDurationZero,
            Expiration        = ExpirationTwoYearAfterStartMinus330,
            MonthlyRecurrence = JanMaySepWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            37,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a Mar/Jul/Nov week-number recurrence with 15-minute repeat, 2-hour duration, and post-window
    /// expiration returns 657 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_ExpirationAfterEndOfWindowInterval15MinDurationTwoHoursMrMarJulNovWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartLocal,
                LocalTz
            ),
            RepeatOptions     = Interval15MinDurationTwoHours,
            Expiration        = ExpirationAfterEndOfWindow,
            MonthlyRecurrence = MarJulNovWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            657,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that a first-half-of-year week-number recurrence with interval-greater-than-duration repeat and
    /// pre-start expiration returns no occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_ExpirationBeforeStartUtcIntervalGreaterThanDurationMrFirstHalfOfYearWeekNum_ReturnsEmptyEnumerable( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartUnspecified,
                DatelineTz
            ),
            RepeatOptions     = IntervalGreaterThanDuration,
            Expiration        = ExpirationBeforeStartUtc,
            MonthlyRecurrence = FirstHalfOfYearWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.AreEqual(
            0,
            occurrences.Count( )
        );
    }

    /// <summary>
    /// Verifies that a last-half-of-year week-number recurrence with half-duration repeat and 1-day expiration returns
    /// 9 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalHalfDurationExpirationOneDayAfterStartLocalMrLastHalfOfYearWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus14,
                LineIslandsTz
            ),
            RepeatOptions     = IntervalHalfDuration,
            Expiration        = ExpirationOneDayAfterStartLocal,
            MonthlyRecurrence = LastHalfOfYearWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            9,
            occurrences
        );
    }

    /// <summary>
    /// Verifies that an all-months, all-week-numbers, all-days week-number recurrence with max repeat and 1-week
    /// expiration returns 59 occurrences.
    /// </summary>
    [TestMethod]
    public void CalculateOccurrences_IntervalMaxDurationMaxExpirationOneWeekAfterStartUnspecifiedMrAllMonthsAllDaysWeekNum_Returns( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                StartPlus13,
                SamoaTz
            ),
            RepeatOptions     = IntervalMaxDurationMax,
            Expiration        = ExpirationOneWeekAfterStartUnspecified,
            MonthlyRecurrence = AllMonthsAllDaysWeekNum
        };
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
            schedule,
            EndOfWindow
        );
        Assert.HasCount(
            59,
            occurrences
        );
    }

    #endregion MonthlyRecurrence

}
