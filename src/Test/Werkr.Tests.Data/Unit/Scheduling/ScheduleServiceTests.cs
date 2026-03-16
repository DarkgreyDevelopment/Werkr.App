using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for the <see cref="ScheduleService"/> class, validating CRUD operations on schedules including daily,
/// weekly, and monthly recurrence types, expiration, repeat options, preview, and full round-trip persistence.
/// </summary>
[TestClass]
public class ScheduleServiceTests {
    /// <summary>
    /// The in-memory SQLite connection used for database operations.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The SQLite-backed <see cref="WerkrDbContext"/> used for test data persistence.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="ScheduleService"/> instance under test.
    /// </summary>
    private ScheduleService _service = null!;

    /// <summary>
    /// Gets or sets the MSTest test context for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly int[] s_expected = [1, 15];

    /// <summary>
    /// Initializes an in-memory SQLite database, creates the schema, and instantiates the <see cref="ScheduleService"/>
    /// with a <see cref="HolidayDateService"/> under test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        _service = new ScheduleService(
            _dbContext,
            new HolidayDateService(
                _dbContext,
                NullLogger<HolidayDateService>.Instance
            ),
            NullLogger<ScheduleService>.Instance
        );
    }

    /// <summary>
    /// Disposes the database context and SQLite connection after each test.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    #region Helpers

    /// <summary>
    /// Creates a minimal <see cref="Schedule"/> with only a start date-time and no recurrence.
    /// </summary>
    private static Schedule MakeMinimalSchedule( string name = "Test Schedule" ) => new( ) {
        DbSchedule = new DbSchedule { Name = name, StopTaskAfterMinutes = 30 },
        StartDateTime = new StartDateTimeInfo {
            Date = new DateOnly(
                2025,
                6,
                15
            ),
            Time = new TimeOnly(
                9,
                0
            ),
            TimeZone = TimeZoneInfo.Utc,
        },
    };

    /// <summary>
    /// Creates a <see cref="Schedule"/> with daily recurrence (every 2 days).
    /// </summary>
    private static Schedule MakeDailySchedule( string name = "Daily Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.DailyRecurrence = new DailyRecurrence { DayInterval = 2 };
        return s;
    }

    /// <summary>
    /// Creates a <see cref="Schedule"/> with weekly recurrence (MWF, every week).
    /// </summary>
    private static Schedule MakeWeeklySchedule( string name = "Weekly Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.WeeklyRecurrence = new WeeklyRecurrence {
            WeekInterval = 1,
            DaysOfWeek = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
        };
        return s;
    }

    /// <summary>
    /// Creates a <see cref="Schedule"/> with monthly day-number recurrence (1st and 15th of January and July).
    /// </summary>
    private static Schedule MakeMonthlyDayNumSchedule( string name = "Monthly DayNum Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.MonthlyRecurrence = new MonthlyRecurrence {
            DayNumbers = [1, 15],
            MonthsOfYear = MonthsOfYear.January | MonthsOfYear.July,
        };
        return s;
    }

    /// <summary>
    /// Creates a <see cref="Schedule"/> with monthly week-day recurrence (second Tuesday of March and September).
    /// </summary>
    private static Schedule MakeMonthlyWeekDaySchedule( string name = "Monthly WeekDay Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.MonthlyRecurrence = new MonthlyRecurrence {
            WeekNumber = WeekNumberWithinMonth.Second,
            DaysOfWeek = DaysOfWeek.Tuesday,
            MonthsOfYear = MonthsOfYear.March | MonthsOfYear.September,
        };
        return s;
    }

    /// <summary>
    /// Creates a fully configured <see cref="Schedule"/> with daily recurrence, expiration, and repeat options.
    /// </summary>
    private static Schedule MakeFullSchedule( string name = "Full Schedule" ) {
        Schedule s = MakeDailySchedule( name );
        s.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly(
                2026,
                12,
                31
            ),
            Time = new TimeOnly(
                23,
                59
            ),
            TimeZone = TimeZoneInfo.Utc,
        };
        s.RepeatOptions = new ScheduleRepeatOptions {
            RepeatIntervalMinutes = 60,
            RepeatDurationMinutes = 480,
        };
        return s;
    }

    #endregion Helpers

    #region CreateAsync

    /// <summary>
    /// Verifies that creating a minimal schedule (start date-time only, no recurrence) persists the entity and returns
    /// it with a non-empty ID and expected name.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_MinimalSchedule_PersistsAndReturns( ) {
        Schedule created = await _service.CreateAsync(
            MakeMinimalSchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created );
        Assert.AreNotEqual(
            Guid.Empty,
            created.DbSchedule.Id
        );
        Assert.AreEqual(
            "Test Schedule",
            created.DbSchedule.Name
        );
        Assert.IsNotNull( created.StartDateTime );
        Assert.AreEqual(
            new DateOnly(
                2025,
                6,
                15
            ),
            created.StartDateTime!.Date
        );
    }

    /// <summary>
    /// Verifies that creating a schedule with daily recurrence persists the <see cref="DailyRecurrence"/> and leaves
    /// weekly/monthly recurrence null.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_WithDailyRecurrence_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync(
            MakeDailySchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created.DailyRecurrence );
        Assert.AreEqual(
            2,
            created.DailyRecurrence!.DayInterval
        );
        Assert.IsNull( created.WeeklyRecurrence );
        Assert.IsNull( created.MonthlyRecurrence );
    }

    /// <summary>
    /// Verifies that creating a schedule with weekly recurrence persists the <see cref="WeeklyRecurrence"/> including
    /// the week interval and selected days of week.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_WithWeeklyRecurrence_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync(
            MakeWeeklySchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created.WeeklyRecurrence );
        Assert.AreEqual(
            1,
            created.WeeklyRecurrence!.WeekInterval
        );
        Assert.AreEqual(
            DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
            created.WeeklyRecurrence.DaysOfWeek
        );
    }

    /// <summary>
    /// Verifies that creating a schedule with monthly day-number recurrence persists the specified day numbers and
    /// months of year.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_WithMonthlyDayNum_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync(
            MakeMonthlyDayNumSchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created.MonthlyRecurrence );
        CollectionAssert.AreEqual(
            s_expected,
            created.MonthlyRecurrence!.DayNumbers
        );
        Assert.AreEqual(
            MonthsOfYear.January | MonthsOfYear.July,
            created.MonthlyRecurrence.MonthsOfYear
        );
    }

    /// <summary>
    /// Verifies that creating a schedule with monthly week-day recurrence persists the week number and day-of-week
    /// selection.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_WithMonthlyWeekDay_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync(
            MakeMonthlyWeekDaySchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created.MonthlyRecurrence );
        Assert.AreEqual(
            WeekNumberWithinMonth.Second,
            created.MonthlyRecurrence!.WeekNumber
        );
        Assert.AreEqual(
            DaysOfWeek.Tuesday,
            created.MonthlyRecurrence.DaysOfWeek
        );
    }

    /// <summary>
    /// Verifies that creating a fully configured schedule (daily recurrence, expiration, and repeat options) persists
    /// all sub-entities correctly.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_FullSchedule_PersistsAllSubEntities( ) {
        Schedule created = await _service.CreateAsync(
            MakeFullSchedule( ),
            TestContext.CancellationToken
        );

        Assert.IsNotNull( created.StartDateTime );
        Assert.IsNotNull( created.Expiration );
        Assert.IsNotNull( created.RepeatOptions );
        Assert.IsNotNull( created.DailyRecurrence );
        Assert.AreEqual(
            60,
            created.RepeatOptions!.RepeatIntervalMinutes
        );
        Assert.AreEqual(
            480,
            created.RepeatOptions.RepeatDurationMinutes
        );
    }

    /// <summary>
    /// Verifies that attempting to create a schedule without a <see cref="StartDateTime"/> throws a <see
    /// cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_NoStartDateTime_ThrowsValidationException( ) {
        Schedule schedule = new( ) {
            DbSchedule = new DbSchedule { Name = "Invalid" },
        };

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            schedule,
            TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that attempting to create a schedule with more than one recurrence type (e.g., daily and weekly) throws
    /// a <see cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_MultipleRecurrenceTypes_ThrowsValidationException( ) {
        Schedule schedule = MakeDailySchedule( "Bad" );
        schedule.WeeklyRecurrence = new WeeklyRecurrence {
            WeekInterval = 1,
            DaysOfWeek = DaysOfWeek.Monday,
        };

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            schedule,
            TestContext.CancellationToken
        ) );
    }

    #endregion CreateAsync

    #region GetByIdAsync

    /// <summary>
    /// Verifies that retrieving a previously created schedule by its ID returns the correct entity with matching ID and
    /// name.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_ExistingSchedule_Returns( ) {
        Schedule created = await _service.CreateAsync(
            MakeDailySchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;

        Schedule? retrieved = await _service.GetByIdAsync(
            id,
            TestContext.CancellationToken
        );

        Assert.IsNotNull( retrieved );
        Assert.AreEqual(
            id,
            retrieved!.DbSchedule.Id
        );
        Assert.AreEqual(
            "Daily Schedule",
            retrieved.DbSchedule.Name
        );
    }

    /// <summary>
    /// Verifies that querying for a non-existent schedule ID returns <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull( ) {
        Schedule? result = await _service.GetByIdAsync(
            Guid.NewGuid( ),
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    #endregion GetByIdAsync

    #region GetByNameAsync

    /// <summary>
    /// Verifies that retrieving a schedule by its name returns the correct entity when a matching name exists.
    /// </summary>
    [TestMethod]
    public async Task GetByNameAsync_ExistingName_Returns( ) {
        _ = await _service.CreateAsync(
            MakeMinimalSchedule( "FindMe" ),
            TestContext.CancellationToken
        );

        Schedule? found = await _service.GetByNameAsync(
            "FindMe",
            TestContext.CancellationToken
        );

        Assert.IsNotNull( found );
        Assert.AreEqual(
            "FindMe",
            found!.DbSchedule.Name
        );
    }

    /// <summary>
    /// Verifies that querying for a non-existent schedule name returns <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task GetByNameAsync_NonExistentName_ReturnsNull( ) {
        Schedule? result = await _service.GetByNameAsync(
            "NoSuchName",
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    #endregion GetByNameAsync

    #region GetAllAsync

    /// <summary>
    /// Verifies that calling <see cref="GetAllAsync"/> on an empty database returns an empty list.
    /// </summary>
    [TestMethod]
    public async Task GetAllAsync_Empty_ReturnsEmptyList( ) {
        IReadOnlyList<Schedule> result = await _service.GetAllAsync( TestContext.CancellationToken );
        Assert.HasCount(
            0,
            result
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetAllAsync"/> returns all schedules after multiple creates.
    /// </summary>
    [TestMethod]
    public async Task GetAllAsync_MultipleSchedules_ReturnsAll( ) {
        _ = await _service.CreateAsync(
            MakeMinimalSchedule( "A" ),
            TestContext.CancellationToken
        );
        _ = await _service.CreateAsync(
            MakeDailySchedule( "B" ),
            TestContext.CancellationToken
        );
        _ = await _service.CreateAsync(
            MakeWeeklySchedule( "C" ),
            TestContext.CancellationToken
        );

        IReadOnlyList<Schedule> all = await _service.GetAllAsync( TestContext.CancellationToken );
        Assert.HasCount(
            3,
            all
        );
    }

    #endregion GetAllAsync

    #region UpdateAsync

    /// <summary>
    /// Verifies that updating a schedule's name persists the new name correctly.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_UpdateCoreName_Reflects( ) {
        Schedule created = await _service.CreateAsync(
            MakeMinimalSchedule( "OldName" ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;

        Schedule update = MakeMinimalSchedule( "NewName" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.AreEqual(
            "NewName",
            updated.DbSchedule.Name
        );
    }

    /// <summary>
    /// Verifies that adding a daily recurrence to a previously non-recurring schedule persists the new <see
    /// cref="DailyRecurrence"/>.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_AddRecurrence_PersistsNewRecurrence( ) {
        Schedule created = await _service.CreateAsync(
            MakeMinimalSchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;

        Schedule update = MakeDailySchedule( "Test Schedule" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( updated.DailyRecurrence );
        Assert.AreEqual(
            2,
            updated.DailyRecurrence!.DayInterval
        );
    }

    /// <summary>
    /// Verifies that changing a schedule's recurrence type from daily to weekly removes the old <see
    /// cref="DailyRecurrence"/> and persists the new <see cref="WeeklyRecurrence"/>.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_ChangeRecurrenceType_RemovesOldAddsNew( ) {
        Schedule daily = await _service.CreateAsync(
            MakeDailySchedule( ),
            TestContext.CancellationToken
        );
        Guid id = daily.DbSchedule.Id;

        Schedule update = MakeWeeklySchedule( "Daily Schedule" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.IsNull( updated.DailyRecurrence );
        Assert.IsNotNull( updated.WeeklyRecurrence );
    }

    /// <summary>
    /// Verifies that adding an expiration to a schedule that previously had none persists the <see
    /// cref="ExpirationDateTimeInfo"/> with the correct date.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_AddExpiration_PersistsExpiration( ) {
        Schedule created = await _service.CreateAsync(
            MakeMinimalSchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;
        Assert.IsNull( created.Expiration );

        Schedule update = MakeMinimalSchedule( );
        update.DbSchedule.Id = id;
        update.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly(
                2026,
                1,
                1
            ),
            Time = new TimeOnly(
                0,
                0
            ),
            TimeZone = TimeZoneInfo.Utc,
        };

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( updated.Expiration );
        Assert.AreEqual(
            new DateOnly(
                2026,
                1,
                1
            ),
            updated.Expiration!.Date
        );
    }

    /// <summary>
    /// Verifies that updating a full schedule without an expiration removes the previously persisted expiration entity.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_RemoveExpiration_RemovesExpiration( ) {
        Schedule created = await _service.CreateAsync(
            MakeFullSchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;
        Assert.IsNotNull( created.Expiration );

        // Remove expiration by not including it
        Schedule update = MakeDailySchedule( "Full Schedule" );
        update.DbSchedule.Id = id;
        update.RepeatOptions = new ScheduleRepeatOptions {
            RepeatIntervalMinutes = 60,
            RepeatDurationMinutes = 480,
        };

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.IsNull( updated.Expiration );
    }

    /// <summary>
    /// Verifies that attempting to update a schedule with a non-existent ID throws a <see
    /// cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        Schedule schedule = MakeMinimalSchedule( );
        schedule.DbSchedule.Id = Guid.NewGuid( );

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.UpdateAsync(
            schedule,
            TestContext.CancellationToken
        ) );
    }

    #endregion UpdateAsync

    #region DeleteAsync

    /// <summary>
    /// Verifies that deleting an existing schedule removes the schedule and all related sub-entities (start date-time,
    /// expiration, repeat options, recurrence) from the database.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_ExistingSchedule_RemovesAllSubEntities( ) {
        Schedule created = await _service.CreateAsync(
            MakeFullSchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;

        await _service.DeleteAsync(
            id,
            TestContext.CancellationToken
        );

        Assert.IsNull( await _service.GetByIdAsync(
            id,
            TestContext.CancellationToken
        ) );
        Assert.HasCount(
            0,
            await _dbContext.Schedules.ToListAsync( TestContext.CancellationToken )
        );
        Assert.HasCount(
            0,
            await _dbContext.StartDateTimeInfos.ToListAsync( TestContext.CancellationToken )
        );
        Assert.HasCount(
            0,
            await _dbContext.ExpirationDateTimeInfos.ToListAsync( TestContext.CancellationToken )
        );
        Assert.HasCount(
            0,
            await _dbContext.ScheduleRepeatOptions.ToListAsync( TestContext.CancellationToken )
        );
        Assert.HasCount(
            0,
            await _dbContext.DailyRecurrences.ToListAsync( TestContext.CancellationToken )
        );
    }

    /// <summary>
    /// Verifies that attempting to delete a schedule with a non-existent ID throws a <see
    /// cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.DeleteAsync(
            Guid.NewGuid( ),
            TestContext.CancellationToken
        ) );
    }

    #endregion DeleteAsync

    #region PreviewOccurrencesAsync

    /// <summary>
    /// Verifies that previewing occurrences for a daily schedule within a given time window returns a non-empty
    /// collection of occurrence date-times.
    /// </summary>
    [TestMethod]
    public async Task PreviewOccurrencesAsync_DailySchedule_ReturnsOccurrences( ) {
        Schedule created = await _service.CreateAsync(
            MakeDailySchedule( ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;

        DateTime windowEnd = new(
            2025,
            7,
            15,
            23,
            59,
            59,
            DateTimeKind.Utc
        );
        ScheduleOccurrenceResult result = await _service.PreviewOccurrencesAsync(
            id,
            windowEnd,
            TestContext.CancellationToken
        );

        Assert.IsNotEmpty( result.Occurrences );
    }

    /// <summary>
    /// Verifies that previewing occurrences for a non-existent schedule ID throws a <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task PreviewOccurrencesAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.PreviewOccurrencesAsync(
            Guid.NewGuid( ),
            DateTime.UtcNow.AddDays( 30 ),
            TestContext.CancellationToken
        ) );
    }

    #endregion PreviewOccurrencesAsync

    #region RoundTrip

    /// <summary>
    /// Performs a full round-trip test: creates a daily schedule, reads it back by ID, updates the name and adds an
    /// expiration, then deletes it and confirms removal.
    /// </summary>
    [TestMethod]
    public async Task RoundTrip_CreateReadUpdateDelete_Succeeds( ) {
        // Create
        Schedule created = await _service.CreateAsync(
            MakeDailySchedule( "RoundTrip" ),
            TestContext.CancellationToken
        );
        Guid id = created.DbSchedule.Id;
        Assert.AreEqual(
            "RoundTrip",
            created.DbSchedule.Name
        );
        Assert.AreEqual(
            2,
            created.DailyRecurrence!.DayInterval
        );

        // Read
        Schedule? read = await _service.GetByIdAsync(
            id,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( read );
        Assert.AreEqual(
            id,
            read!.DbSchedule.Id
        );

        // Update — change name + add expiration
        Schedule update = MakeDailySchedule( "RoundTripUpdated" );
        update.DbSchedule.Id = id;
        update.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly(
                2026,
                6,
                15
            ),
            Time = new TimeOnly(
                17,
                0
            ),
            TimeZone = TimeZoneInfo.Utc,
        };

        Schedule updated = await _service.UpdateAsync(
            update,
            TestContext.CancellationToken
        );
        Assert.AreEqual(
            "RoundTripUpdated",
            updated.DbSchedule.Name
        );
        Assert.IsNotNull( updated.Expiration );

        // Delete
        await _service.DeleteAsync(
            id,
            TestContext.CancellationToken
        );
        Assert.IsNull( await _service.GetByIdAsync(
            id,
            TestContext.CancellationToken
        ) );
    }

    #endregion RoundTrip
}
