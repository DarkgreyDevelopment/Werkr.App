using Werkr.Agent.Scheduling;
using Werkr.Common.Protos;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;

namespace Werkr.Tests.Agent.Scheduling;

/// <summary>
/// Unit tests for the <see cref="ScheduleEvaluatorService"/> and its related
/// types. Covers mapping from <see cref="ScheduleDefinition"/> protobuf
/// messages to the internal <see cref="Schedule"/> model (basic fields,
/// expiration, daily/weekly/monthly recurrence, repeat options, holiday
/// calendars, edge cases), ordering and comparison of
/// <see cref="ScheduleEvaluatorService.FireQueueEntry"/> instances, and the
/// output-path helper in <see cref="AgentJobOutputWriter"/>.
/// </summary>
[TestClass]
public class ScheduleEvaluatorServiceTests {

    #region MapProtoToSchedule

    /// <summary>
    /// Verifies that a minimal <see cref="ScheduleDefinition"/> with only
    /// required fields maps to a <see cref="Schedule"/> containing the correct
    /// start date, time, time zone, and
    /// <see cref="Schedule.StopTaskAfterMinutes"/>, while leaving optional
    /// recurrence and repeat properties <see langword="null"/>.
    /// </summary>
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
        Assert.AreEqual(
            new DateOnly(
                2025,
                6,
                15
            ),
            schedule.StartDateTime.Date
        );
        Assert.AreEqual(
            new TimeOnly( 8, 30 ),
            schedule.StartDateTime.Time
        );
        Assert.AreEqual(
            "UTC",
            schedule.StartDateTime.TimeZone.Id
        );
        Assert.AreEqual(
            60,
            schedule.DbSchedule.StopTaskAfterMinutes
        );
        Assert.IsNull( schedule.Expiration );
        Assert.IsNull( schedule.DailyRecurrence );
        Assert.IsNull( schedule.WeeklyRecurrence );
        Assert.IsNull( schedule.MonthlyRecurrence );
        Assert.IsNull( schedule.RepeatOptions );
    }

    /// <summary>
    /// Verifies that when expiration date/time fields are present on the
    /// proto, they are correctly mapped to the
    /// <see cref="Schedule.Expiration"/> property.
    /// </summary>
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
        Assert.AreEqual(
            new DateOnly(
                2025,
                12,
                31
            ),
            schedule.Expiration.Date
        );
        Assert.AreEqual(
            new TimeOnly( 23, 59 ),
            schedule.Expiration.Time
        );
    }

    /// <summary>
    /// Verifies that a daily recurrence definition with a non-zero
    /// <see cref="DailyRecurrence.DayInterval"/> is mapped to the
    /// <see cref="Schedule.DailyRecurrence"/> property, and weekly/monthly
    /// remain <see langword="null"/>.
    /// </summary>
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
        Assert.AreEqual(
            3,
            schedule.DailyRecurrence.DayInterval
        );
        Assert.IsNull( schedule.WeeklyRecurrence );
        Assert.IsNull( schedule.MonthlyRecurrence );
    }

    /// <summary>
    /// Verifies that a weekly recurrence with a week interval and specific
    /// days of the week is correctly mapped to
    /// <see cref="Schedule.WeeklyRecurrence"/>.
    /// </summary>
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
        Assert.AreEqual(
            2,
            schedule.WeeklyRecurrence.WeekInterval
        );
        Assert.AreEqual(
            DaysOfWeek.Monday | DaysOfWeek.Friday,
            schedule.WeeklyRecurrence.DaysOfWeek
        );
    }

    /// <summary>
    /// Verifies that a monthly recurrence with months-of-year, week number,
    /// and days-of-week is correctly mapped to
    /// <see cref="Schedule.MonthlyRecurrence"/>.
    /// </summary>
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
        Assert.AreEqual(
            MonthsOfYear.January | MonthsOfYear.July,
            schedule.MonthlyRecurrence.MonthsOfYear
        );
        Assert.AreEqual(
            WeekNumberWithinMonth.First,
            schedule.MonthlyRecurrence.WeekNumber
        );
        Assert.AreEqual(
            DaysOfWeek.Monday,
            schedule.MonthlyRecurrence.DaysOfWeek
        );
    }

    /// <summary>
    /// Verifies that repeat options with non-zero interval and duration
    /// minutes are mapped to <see cref="Schedule.RepeatOptions"/>.
    /// </summary>
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
        Assert.AreEqual(
            15,
            schedule.RepeatOptions.RepeatIntervalMinutes
        );
        Assert.AreEqual(
            120,
            schedule.RepeatOptions.RepeatDurationMinutes
        );
    }

    /// <summary>
    /// Verifies that a daily recurrence with
    /// <see cref="DailyRecurrence.DayInterval"/> of zero is treated as absent
    /// and <see cref="Schedule.DailyRecurrence"/> remains
    /// <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that repeat options with both interval and duration set to
    /// zero are treated as absent and <see cref="Schedule.RepeatOptions"/>
    /// remains <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that an unparseable schedule-id string results in
    /// <see cref="Guid.Empty"/> being stored in the database schedule entity.
    /// </summary>
    [TestMethod]
    public void MapProtoToSchedule_InvalidScheduleId_UsesEmptyGuid( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = "not-a-guid",
            StartDate = "2025-06-15",
            StartTime = "08:30",
            TimeZoneId = "UTC",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.AreEqual(
            Guid.Empty,
            schedule.DbSchedule.Id
        );
    }

    /// <summary>
    /// Verifies that when the start-time string is empty the schedule
    /// defaults to <see cref="TimeOnly.MinValue"/> (midnight).
    /// </summary>
    [TestMethod]
    public void MapProtoToSchedule_EmptyTime_DefaultsToMidnight( ) {
        ScheduleDefinition proto = new( ) {
            ScheduleId = Guid.NewGuid( ).ToString( ),
            StartDate = "2025-06-15",
            StartTime = string.Empty,
            TimeZoneId = "UTC",
        };

        Schedule schedule = ScheduleEvaluatorService.MapProtoToSchedule( proto );

        Assert.AreEqual(
            TimeOnly.MinValue,
            schedule.StartDateTime!.Time
        );
    }

    #endregion MapProtoToSchedule

    #region MapProtoToSchedule — Holiday Fields

    /// <summary>
    /// Verifies that setting
    /// <see cref="ScheduleDefinition.HasHolidayCalendar"/> to
    /// <see langword="true"/> with mode "Blocklist" populates
    /// <see cref="Schedule.HolidayCalendar"/> and sets the mode to
    /// <see cref="HolidayCalendarMode.Blocklist"/>.
    /// </summary>
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
        Assert.AreEqual(
            HolidayCalendarMode.Blocklist,
            schedule.HolidayCalendarMode
        );
    }

    /// <summary>
    /// Verifies that setting
    /// <see cref="ScheduleDefinition.HasHolidayCalendar"/> to
    /// <see langword="true"/> with mode "Allowlist" populates
    /// <see cref="Schedule.HolidayCalendar"/> and sets the mode to
    /// <see cref="HolidayCalendarMode.Allowlist"/>.
    /// </summary>
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
        Assert.AreEqual(
            HolidayCalendarMode.Allowlist,
            schedule.HolidayCalendarMode
        );
    }

    /// <summary>
    /// Verifies that when
    /// <see cref="ScheduleDefinition.HasHolidayCalendar"/> is
    /// <see langword="false"/>, both <see cref="Schedule.HolidayCalendar"/>
    /// and <see cref="Schedule.HolidayCalendarMode"/> remain
    /// <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that an unrecognizable holiday-calendar mode string still
    /// creates the calendar object but leaves
    /// <see cref="Schedule.HolidayCalendarMode"/> as <see langword="null"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="ScheduleEvaluatorService.FireQueueEntry"/>
    /// instances are ordered primarily by fire-time, with earlier times
    /// sorting before later times.
    /// </summary>
    [TestMethod]
    public void FireQueueEntry_OrdersByFireTime( ) {
        DateTime earlier = DateTime.UtcNow;
        DateTime later = earlier.AddMinutes( 10 );

        ScheduleEvaluatorService.FireQueueEntry entry1 = new(
            earlier,
            CreateTaskDef( 1 ),
            null
        );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new(
            later,
            CreateTaskDef( 2 ),
            null
        );

        Assert.IsLessThan(
            0,
            entry1.CompareTo( entry2 )
        );
        Assert.IsGreaterThan(
            0,
            entry2.CompareTo( entry1 )
        );
    }

    /// <summary>
    /// Verifies that when two entries share the same fire-time, comparison falls back to task-id ordering.
    /// </summary>
    [TestMethod]
    public void FireQueueEntry_SameTime_BreaksTiesById( ) {
        DateTime time = DateTime.UtcNow;

        ScheduleEvaluatorService.FireQueueEntry entry1 = new(
            time,
            CreateTaskDef( 1 ),
            null
        );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new(
            time,
            CreateTaskDef( 5 ),
            null
        );

        Assert.IsLessThan(
            0,
            entry1.CompareTo( entry2 )
        );
    }

    /// <summary>
    /// Verifies that two entries with identical fire-time and task-id compare as equal (zero).
    /// </summary>
    [TestMethod]
    public void FireQueueEntry_SameTimeAndId_ReturnsZero( ) {
        DateTime time = DateTime.UtcNow;

        ScheduleEvaluatorService.FireQueueEntry entry1 = new(
            time,
            CreateTaskDef( 3 ),
            null
        );
        ScheduleEvaluatorService.FireQueueEntry entry2 = new(
            time,
            CreateTaskDef( 3 ),
            null
        );

        Assert.AreEqual(
            0,
            entry1.CompareTo( entry2 )
        );
    }

    /// <summary>
    /// Verifies that comparing a
    /// <see cref="ScheduleEvaluatorService.FireQueueEntry"/> to
    /// <see langword="null"/> returns a positive value (non-null sorts
    /// after null).
    /// </summary>
    [TestMethod]
    public void FireQueueEntry_NullOther_ReturnsPositive( ) {
        DateTime time = DateTime.UtcNow;
        ScheduleEvaluatorService.FireQueueEntry entry = new(
            time,
            CreateTaskDef( 1 ),
            null
        );

        Assert.IsGreaterThan(
            0,
            entry.CompareTo( null )
        );
    }

    #endregion FireQueueEntry Comparisons

    #region AgentJobOutputWriter

    /// <summary>
    /// Verifies that
    /// <see cref="AgentJobOutputWriter.GetRelativeOutputPath"/> returns the
    /// job-id formatted as a GUID followed by the ".log" extension.
    /// </summary>
    [TestMethod]
    public void GetRelativeOutputPath_ReturnsCorrectFormat( ) {
        Guid jobId = Guid.Parse( "12345678-1234-1234-1234-123456789abc" );
        string path = AgentJobOutputWriter.GetRelativeOutputPath( jobId );
        Assert.AreEqual(
            "12345678-1234-1234-1234-123456789abc.log",
            path
        );
    }

    #endregion AgentJobOutputWriter

    #region Helpers

    /// <summary>
    /// Creates a minimal <see cref="ScheduledTaskDefinition"/> proto with
    /// the given task identifier for use in
    /// <see cref="ScheduleEvaluatorService.FireQueueEntry"/> comparison tests.
    /// </summary>
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
