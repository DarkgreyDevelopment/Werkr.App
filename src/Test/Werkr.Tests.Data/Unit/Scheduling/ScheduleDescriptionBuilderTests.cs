using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for the <see cref="ScheduleDescriptionBuilder"/> class, validating human-readable schedule descriptions
/// for once, daily, weekly, monthly, repeat, and expiration configurations.
/// </summary>
[TestClass]
public class ScheduleDescriptionBuilderTests {

    #region Helpers

    /// <summary>
    /// Creates a <see cref="StartDateTimeInfo"/> from the given date-time and time zone.
    /// </summary>
    private static StartDateTimeInfo MakeStart( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    /// <summary>
    /// Creates an <see cref="ExpirationDateTimeInfo"/> from the given date-time and time zone.
    /// </summary>
    private static ExpirationDateTimeInfo MakeExpiration( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    /// <summary>
    /// Creates a minimal <see cref="DbSchedule"/> for test schedule construction.
    /// </summary>
    private static DbSchedule TestDb( ) => new( ) { Name = "Test" };

    /// <summary>
    /// A fixed test date (2025-03-15 09:00 UTC) used across all tests.
    /// </summary>
    private static readonly DateTime s_testDate = new(
        2025,
        3,
        15,
        9,
        0,
        0,
        DateTimeKind.Utc
    );

    #endregion Helpers

    /// <summary>
    /// Verifies that a one-time schedule description starts with "Once on" and includes the date and time.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_OnceSchedule_ReturnsOnceWithDate( ) {
        Schedule schedule = new() {
            /// <summary>
            /// Creates a minimal <see cref="DbSchedule"/> for test schedule construction.
            /// </summary>
            DbSchedule    = TestDb(),
            /// <summary>
            /// A fixed test date (2025-03-15 09:00 UTC) used across all tests.
            /// </summary>
            StartDateTime = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            )
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Once on 2025-03-15",
            desc
        );
        Assert.Contains(
            "9:00 AM",
            desc
        );
        Assert.Contains(
            "UTC",
            desc
        );
    }

    /// <summary>
    /// Verifies that a daily schedule with interval 1 starts with "Daily".
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_Daily_ReturnsDailyDescription( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            DailyRecurrence = new() { DayInterval = 1 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Daily",
            desc
        );
    }

    /// <summary>
    /// Verifies that a daily schedule with interval 3 starts with "Every 3 days".
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_EveryThreeDays_ReturnsIntervalDescription( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            DailyRecurrence = new() { DayInterval = 3 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Every 3 days",
            desc
        );
    }

    /// <summary>
    /// Verifies that a weekly MWF schedule description starts with "Weekly on" and includes abbreviated day names.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_WeeklyMWF_ReturnsWeeklyWithDays( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            WeeklyRecurrence = new() {
                WeekInterval = 1,
                DaysOfWeek   = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Weekly on",
            desc
        );
        Assert.Contains(
            "Mon",
            desc
        );
        Assert.Contains(
            "Wed",
            desc
        );
        Assert.Contains(
            "Fri",
            desc
        );
    }

    /// <summary>
    /// Verifies that a bi-weekly schedule description starts with "Every 2 weeks on".
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_BiWeekly_ReturnsEveryTwoWeeks( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            WeeklyRecurrence = new() {
                WeekInterval = 2,
                DaysOfWeek   = DaysOfWeek.Monday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Every 2 weeks on",
            desc
        );
    }

    /// <summary>
    /// Verifies that a monthly day-number schedule mentions ordinal day numbers (1st, 15th).
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_MonthlyDayNumbers_ReturnsDayNumDescription( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            MonthlyRecurrence = new() {
                MonthsOfYear = MonthsOfYear.January | MonthsOfYear.March,
                DayNumbers   = [1, 15]
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Monthly on the",
            desc
        );
        Assert.Contains(
            "1st",
            desc
        );
        Assert.Contains(
            "15th",
            desc
        );
    }

    /// <summary>
    /// Verifies that a monthly week-based schedule mentions the week ordinal and abbreviated day name.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_MonthlyWeekBased_ReturnsWeekDayDescription( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            MonthlyRecurrence = new() {
                MonthsOfYear = MonthsOfYear.January,
                WeekNumber   = WeekNumberWithinMonth.First,
                DaysOfWeek   = DaysOfWeek.Monday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith(
            "Monthly on the",
            desc
        );
        Assert.Contains(
            "Mon",
            desc
        );
    }

    /// <summary>
    /// Verifies that repeat options include interval and duration information in the description.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_WithRepeatOptions_ContainsRepeating( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            RepeatOptions = new() { RepeatIntervalMinutes = 15, RepeatDurationMinutes = 120 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains(
            "repeating every 15 min",
            desc
        );
        Assert.Contains(
            "for 2 hours",
            desc
        );
    }

    /// <summary>
    /// Verifies that an expiration date appends "until" with the expiration date to the description.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_WithExpiration_ContainsUntil( ) {
        DateTime expDate = new(
            2025,
            12,
            31,
            17,
            0,
            0,
            DateTimeKind.Utc
        );
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            Expiration    = MakeExpiration(
                expDate,
                TimeZoneInfo.Utc
            )
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains(
            "until 2025-12-31",
            desc
        );
    }

    /// <summary>
    /// Verifies that indefinite repeat duration includes "indefinitely" in the description.
    /// </summary>
    [TestMethod]
    public void GetFriendlyDescription_RepeatIndefinitely_ContainsIndefinitely( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart(
                s_testDate,
                TimeZoneInfo.Utc
            ),
            RepeatOptions = new() { RepeatIntervalMinutes = 60, RepeatDurationMinutes = -1 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains(
            "indefinitely",
            desc
        );
    }

}
