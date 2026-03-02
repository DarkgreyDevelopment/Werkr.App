using Werkr.Agent.Scheduling;
using Werkr.Common.Protos;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;

namespace Werkr.Tests.Agent.Scheduling;

[TestClass]
public class ScheduleEvaluatorServiceTests {

    #region MapProtoToSchedule

    [TestMethod]
    public void MapProtoToSchedule_BasicSchedule_MapsCorrectly( ) {
        // Arrange
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            StopTaskAfterMinutes = 60,
        };

        // Act
        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        // Assert
        Assert.IsNotNull( schedule );
        Assert.IsNotNull( schedule.StartDateTime );
        Assert.AreEqual( new DateOnly( 2025, 6, 15 ), schedule.StartDateTime.Date );
        Assert.AreEqual( new TimeOnly( 8, 30 ), schedule.StartDateTime.Time );
        Assert.AreEqual( "UTC", schedule.StartDateTime.TimeZone.Id );
        Assert.AreEqual( 60, schedule.DbSchedule.StopTaskAfterMinutes );
        Assert.IsNull( schedule.Expiration );
        Assert.IsNull( schedule.DailyRecurrence );
        Assert.IsNull( schedule.WeeklyRecurrence );
        Assert.IsNull( schedule.MonthlyRecurrence );
        Assert.IsNull( schedule.RepeatOptions );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithExpiration_MapsCorrectly( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            ExpirationDate = "2025-12-31",
            ExpirationTime = "23:59",
            ExpirationTimeZoneId = "UTC",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.Expiration );
        Assert.AreEqual( new DateOnly( 2025, 12, 31 ), schedule.Expiration.Date );
        Assert.AreEqual( new TimeOnly( 23, 59 ), schedule.Expiration.Time );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithDailyRecurrence_MapsCorrectly( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Daily = new DailyRecurrenceDef { DayInterval = 3 },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.DailyRecurrence );
        Assert.AreEqual( 3, schedule.DailyRecurrence.DayInterval );
        Assert.IsNull( schedule.WeeklyRecurrence );
        Assert.IsNull( schedule.MonthlyRecurrence );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithWeeklyRecurrence_MapsCorrectly( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Weekly = new WeeklyRecurrenceDef {
                WeekInterval = 2,
                RecurrenceDays = (int) ( DaysOfWeek.Monday | DaysOfWeek.Friday ),
            },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.WeeklyRecurrence );
        Assert.AreEqual( 2, schedule.WeeklyRecurrence.WeekInterval );
        Assert.AreEqual( DaysOfWeek.Monday | DaysOfWeek.Friday, schedule.WeeklyRecurrence.DaysOfWeek );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithMonthlyRecurrence_MapsCorrectly( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Monthly = new MonthlyRecurrenceDef {
                MonthsOfYear = (int) ( MonthsOfYear.January | MonthsOfYear.July ),
                WeekNumber = (int) WeekNumberWithinMonth.First,
                DaysOfWeek = (int) DaysOfWeek.Monday,
            },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.MonthlyRecurrence );
        Assert.AreEqual( MonthsOfYear.January | MonthsOfYear.July, schedule.MonthlyRecurrence.MonthsOfYear );
        Assert.AreEqual( WeekNumberWithinMonth.First, schedule.MonthlyRecurrence.WeekNumber );
        Assert.AreEqual( DaysOfWeek.Monday, schedule.MonthlyRecurrence.DaysOfWeek );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithRepeatOptions_MapsCorrectly( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Repeat = new RepeatOptionsDef {
                IntervalMinutes = 15,
                DurationMinutes = 120,
            },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.RepeatOptions );
        Assert.AreEqual( 15, schedule.RepeatOptions.RepeatIntervalMinutes );
        Assert.AreEqual( 120, schedule.RepeatOptions.RepeatDurationMinutes );
    }

    [TestMethod]
    public void MapProtoToSchedule_EmptyDailyRecurrence_DoesNotMap( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Daily = new DailyRecurrenceDef { DayInterval = 0 },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNull( schedule.DailyRecurrence );
    }

    [TestMethod]
    public void MapProtoToSchedule_EmptyRepeatOptions_DoesNotMap( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            Repeat = new RepeatOptionsDef { IntervalMinutes = 0, DurationMinutes = 0 },
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNull( schedule.RepeatOptions );
    }

    [TestMethod]
    public void MapProtoToSchedule_InvalidScheduleId_UsesEmptyGuid( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = "not-a-guid",
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.AreEqual( Guid.Empty, schedule.DbSchedule.Id );
    }

    [TestMethod]
    public void MapProtoToSchedule_EmptyTime_DefaultsToMidnight( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "",
            TimeZoneId = "UTC",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.AreEqual( TimeOnly.MinValue, schedule.StartDateTime!.Time );
    }

    #endregion MapProtoToSchedule

    #region MapProtoToSchedule — Holiday Fields

    [TestMethod]
    public void MapProtoToSchedule_WithHolidayCalendar_Blocklist_SetsProperties( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            HasHolidayCalendar = true,
            HolidayCalendarMode = "Blocklist",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.HolidayCalendar );
        Assert.AreEqual( HolidayCalendarMode.Blocklist, schedule.HolidayCalendarMode );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithHolidayCalendar_Allowlist_SetsProperties( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            HasHolidayCalendar = true,
            HolidayCalendarMode = "Allowlist",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.HolidayCalendar );
        Assert.AreEqual( HolidayCalendarMode.Allowlist, schedule.HolidayCalendarMode );
    }

    [TestMethod]
    public void MapProtoToSchedule_WithoutHolidayCalendar_LeavesNull( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            HasHolidayCalendar = false,
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNull( schedule.HolidayCalendar );
        Assert.IsNull( schedule.HolidayCalendarMode );
    }

    [TestMethod]
    public void MapProtoToSchedule_InvalidMode_SetsCalendarButModeNull( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
            HasHolidayCalendar = true,
            HolidayCalendarMode = "InvalidValue",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.IsNotNull( schedule.HolidayCalendar );
        Assert.IsNull( schedule.HolidayCalendarMode );
    }

    #endregion MapProtoToSchedule — Holiday Fields

    #region FireQueueEntry Comparisons

    [TestMethod]
    public void FireQueueEntry_OrdersByFireTime( ) {
        DateTime earlier = DateTime.UtcNow;
        DateTime later = earlier.AddMinutes( 10 );

        ScheduleEvaluatorService.FireQueueEntry entry1 = new( earlier, CreateTaskDef( 1 ), null );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new( later, CreateTaskDef( 2 ), null );

        Assert.IsLessThan( 0, entry1.CompareTo( entry2 ) );
        Assert.IsGreaterThan( 0, entry2.CompareTo( entry1 ) );
    }

    [TestMethod]
    public void FireQueueEntry_SameTime_BreaksTiesById( ) {
        DateTime time = DateTime.UtcNow;

        ScheduleEvaluatorService.FireQueueEntry entry1 = new( time, CreateTaskDef( 1 ), null );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new( time, CreateTaskDef( 5 ), null );

        Assert.IsLessThan( 0, entry1.CompareTo( entry2 ) );
    }

    [TestMethod]
    public void FireQueueEntry_SameTimeAndId_ReturnsZero( ) {
        DateTime time = DateTime.UtcNow;

        ScheduleEvaluatorService.FireQueueEntry entry1 = new( time, CreateTaskDef( 3 ), null );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new( time, CreateTaskDef( 3 ), null );

        Assert.AreEqual( 0, entry1.CompareTo( entry2 ) );
    }

    [TestMethod]
    public void FireQueueEntry_NullOther_ReturnsPositive( ) {
        DateTime time = DateTime.UtcNow;
        ScheduleEvaluatorService.FireQueueEntry entry = new( time, CreateTaskDef( 1 ), null );

        Assert.IsGreaterThan( 0, entry.CompareTo( null ) );
    }

    #endregion FireQueueEntry Comparisons

    #region AgentJobOutputWriter

    [TestMethod]
    public void GetRelativeOutputPath_ReturnsCorrectFormat( ) {
        Guid jobId = Guid.Parse( "12345678-1234-1234-1234-123456789abc" );
        string path = AgentJobOutputWriter.GetRelativeOutputPath( jobId );
        Assert.AreEqual( "12345678-1234-1234-1234-123456789abc.log", path );
    }

    #endregion AgentJobOutputWriter

    #region Helpers

    private static ScheduledTaskDefinition CreateTaskDef( long taskId ) => new( ) {
        TaskId = taskId,
        Name = $"TestTask{taskId}",
        ActionType = 0,
        Content = "echo test",
        TimeoutMinutes = 5,
        SyncIntervalMinutes = 30,
    };

    #endregion Helpers
}
