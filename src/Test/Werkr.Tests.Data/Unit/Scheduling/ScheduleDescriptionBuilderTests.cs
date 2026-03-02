using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

[TestClass]
public class ScheduleDescriptionBuilderTests {

    #region Helpers

    private static StartDateTimeInfo MakeStart( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    private static ExpirationDateTimeInfo MakeExpiration( DateTime dt, TimeZoneInfo tz ) => new( ) {
        Date = DateOnly.FromDateTime( dt ),
        Time = TimeOnly.FromDateTime( dt ),
        TimeZone = tz
    };

    private static DbSchedule TestDb( ) => new( ) { Name = "Test" };

    private static readonly DateTime s_testDate = new( 2025, 3, 15, 9, 0, 0, DateTimeKind.Utc );

    #endregion Helpers


    [TestMethod]
    public void GetFriendlyDescription_OnceSchedule_ReturnsOnceWithDate( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart( s_testDate, TimeZoneInfo.Utc )
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Once on 2025-03-15", desc );
        Assert.Contains( "9:00 AM", desc );
        Assert.Contains( "UTC", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_Daily_ReturnsDailyDescription( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            DailyRecurrence = new() { DayInterval = 1 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Daily", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_EveryThreeDays_ReturnsIntervalDescription( ) {
        Schedule schedule = new() {
            DbSchedule      = TestDb(),
            StartDateTime   = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            DailyRecurrence = new() { DayInterval = 3 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Every 3 days", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_WeeklyMWF_ReturnsWeeklyWithDays( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            WeeklyRecurrence = new() {
                WeekInterval = 1,
                DaysOfWeek   = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Weekly on", desc );
        Assert.Contains( "Mon", desc );
        Assert.Contains( "Wed", desc );
        Assert.Contains( "Fri", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_BiWeekly_ReturnsEveryTwoWeeks( ) {
        Schedule schedule = new() {
            DbSchedule       = TestDb(),
            StartDateTime    = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            WeeklyRecurrence = new() {
                WeekInterval = 2,
                DaysOfWeek   = DaysOfWeek.Monday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Every 2 weeks on", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_MonthlyDayNumbers_ReturnsDayNumDescription( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            MonthlyRecurrence = new() {
                MonthsOfYear = MonthsOfYear.January | MonthsOfYear.March,
                DayNumbers   = [1, 15]
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Monthly on the", desc );
        Assert.Contains( "1st", desc );
        Assert.Contains( "15th", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_MonthlyWeekBased_ReturnsWeekDayDescription( ) {
        Schedule schedule = new() {
            DbSchedule        = TestDb(),
            StartDateTime     = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            MonthlyRecurrence = new() {
                MonthsOfYear = MonthsOfYear.January,
                WeekNumber   = WeekNumberWithinMonth.First,
                DaysOfWeek   = DaysOfWeek.Monday
            }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.StartsWith( "Monthly on the", desc );
        Assert.Contains( "Mon", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_WithRepeatOptions_ContainsRepeating( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            RepeatOptions = new() { RepeatIntervalMinutes = 15, RepeatDurationMinutes = 120 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains( "repeating every 15 min", desc );
        Assert.Contains( "for 2 hours", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_WithExpiration_ContainsUntil( ) {
        DateTime expDate = new( 2025, 12, 31, 17, 0, 0, DateTimeKind.Utc );
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            Expiration    = MakeExpiration( expDate, TimeZoneInfo.Utc )
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains( "until 2025-12-31", desc );
    }

    [TestMethod]
    public void GetFriendlyDescription_RepeatIndefinitely_ContainsIndefinitely( ) {
        Schedule schedule = new() {
            DbSchedule    = TestDb(),
            StartDateTime = MakeStart( s_testDate, TimeZoneInfo.Utc ),
            RepeatOptions = new() { RepeatIntervalMinutes = 60, RepeatDurationMinutes = -1 }
        };
        string desc = ScheduleDescriptionBuilder.GetFriendlyDescription( schedule );
        Assert.Contains( "indefinitely", desc );
    }

}
