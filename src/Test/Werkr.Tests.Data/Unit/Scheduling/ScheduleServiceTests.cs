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

[TestClass]
public class ScheduleServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private ScheduleService _service = null!;

    public TestContext TestContext { get; set; } = null!;

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
            new HolidayDateService( _dbContext, NullLogger<HolidayDateService>.Instance ),
            NullLogger<ScheduleService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    #region Helpers

    private static Schedule MakeMinimalSchedule( string name = "Test Schedule" ) => new( ) {
        DbSchedule = new DbSchedule { Name = name, StopTaskAfterMinutes = 30 },
        StartDateTime = new StartDateTimeInfo {
            Date = new DateOnly( 2025, 6, 15 ),
            Time = new TimeOnly( 9, 0 ),
            TimeZone = TimeZoneInfo.Utc,
        },
    };

    private static Schedule MakeDailySchedule( string name = "Daily Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.DailyRecurrence = new DailyRecurrence { DayInterval = 2 };
        return s;
    }

    private static Schedule MakeWeeklySchedule( string name = "Weekly Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.WeeklyRecurrence = new WeeklyRecurrence {
            WeekInterval = 1,
            DaysOfWeek = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
        };
        return s;
    }

    private static Schedule MakeMonthlyDayNumSchedule( string name = "Monthly DayNum Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.MonthlyRecurrence = new MonthlyRecurrence {
            DayNumbers = [1, 15],
            MonthsOfYear = MonthsOfYear.January | MonthsOfYear.July,
        };
        return s;
    }

    private static Schedule MakeMonthlyWeekDaySchedule( string name = "Monthly WeekDay Schedule" ) {
        Schedule s = MakeMinimalSchedule( name );
        s.MonthlyRecurrence = new MonthlyRecurrence {
            WeekNumber = WeekNumberWithinMonth.Second,
            DaysOfWeek = DaysOfWeek.Tuesday,
            MonthsOfYear = MonthsOfYear.March | MonthsOfYear.September,
        };
        return s;
    }

    private static Schedule MakeFullSchedule( string name = "Full Schedule" ) {
        Schedule s = MakeDailySchedule( name );
        s.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly( 2026, 12, 31 ),
            Time = new TimeOnly( 23, 59 ),
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

    [TestMethod]
    public async Task CreateAsync_MinimalSchedule_PersistsAndReturns( ) {
        Schedule created = await _service.CreateAsync( MakeMinimalSchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created );
        Assert.AreNotEqual( Guid.Empty, created.DbSchedule.Id );
        Assert.AreEqual( "Test Schedule", created.DbSchedule.Name );
        Assert.IsNotNull( created.StartDateTime );
        Assert.AreEqual( new DateOnly( 2025, 6, 15 ), created.StartDateTime!.Date );
    }

    [TestMethod]
    public async Task CreateAsync_WithDailyRecurrence_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync( MakeDailySchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created.DailyRecurrence );
        Assert.AreEqual( 2, created.DailyRecurrence!.DayInterval );
        Assert.IsNull( created.WeeklyRecurrence );
        Assert.IsNull( created.MonthlyRecurrence );
    }

    [TestMethod]
    public async Task CreateAsync_WithWeeklyRecurrence_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync( MakeWeeklySchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created.WeeklyRecurrence );
        Assert.AreEqual( 1, created.WeeklyRecurrence!.WeekInterval );
        Assert.AreEqual(
            DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday,
            created.WeeklyRecurrence.DaysOfWeek );
    }

    [TestMethod]
    public async Task CreateAsync_WithMonthlyDayNum_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync( MakeMonthlyDayNumSchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created.MonthlyRecurrence );
        CollectionAssert.AreEqual( new[] { 1, 15 }, created.MonthlyRecurrence!.DayNumbers );
        Assert.AreEqual(
            MonthsOfYear.January | MonthsOfYear.July,
            created.MonthlyRecurrence.MonthsOfYear );
    }

    [TestMethod]
    public async Task CreateAsync_WithMonthlyWeekDay_PersistsRecurrence( ) {
        Schedule created = await _service.CreateAsync( MakeMonthlyWeekDaySchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created.MonthlyRecurrence );
        Assert.AreEqual( WeekNumberWithinMonth.Second, created.MonthlyRecurrence!.WeekNumber );
        Assert.AreEqual( DaysOfWeek.Tuesday, created.MonthlyRecurrence.DaysOfWeek );
    }

    [TestMethod]
    public async Task CreateAsync_FullSchedule_PersistsAllSubEntities( ) {
        Schedule created = await _service.CreateAsync( MakeFullSchedule( ), TestContext.CancellationToken );

        Assert.IsNotNull( created.StartDateTime );
        Assert.IsNotNull( created.Expiration );
        Assert.IsNotNull( created.RepeatOptions );
        Assert.IsNotNull( created.DailyRecurrence );
        Assert.AreEqual( 60, created.RepeatOptions!.RepeatIntervalMinutes );
        Assert.AreEqual( 480, created.RepeatOptions.RepeatDurationMinutes );
    }

    [TestMethod]
    public async Task CreateAsync_NoStartDateTime_ThrowsValidationException( ) {
        Schedule schedule = new( ) {
            DbSchedule = new DbSchedule { Name = "Invalid" },
        };

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) =>
            _service.CreateAsync( schedule, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task CreateAsync_MultipleRecurrenceTypes_ThrowsValidationException( ) {
        Schedule schedule = MakeDailySchedule( "Bad" );
        schedule.WeeklyRecurrence = new WeeklyRecurrence {
            WeekInterval = 1,
            DaysOfWeek = DaysOfWeek.Monday,
        };

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) =>
            _service.CreateAsync( schedule, TestContext.CancellationToken ) );
    }

    #endregion CreateAsync

    #region GetByIdAsync

    [TestMethod]
    public async Task GetByIdAsync_ExistingSchedule_Returns( ) {
        Schedule created = await _service.CreateAsync( MakeDailySchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;

        Schedule? retrieved = await _service.GetByIdAsync( id, TestContext.CancellationToken );

        Assert.IsNotNull( retrieved );
        Assert.AreEqual( id, retrieved!.DbSchedule.Id );
        Assert.AreEqual( "Daily Schedule", retrieved.DbSchedule.Name );
    }

    [TestMethod]
    public async Task GetByIdAsync_NonExistentId_ReturnsNull( ) {
        Schedule? result = await _service.GetByIdAsync( Guid.NewGuid( ), TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    #endregion GetByIdAsync

    #region GetByNameAsync

    [TestMethod]
    public async Task GetByNameAsync_ExistingName_Returns( ) {
        _ = await _service.CreateAsync( MakeMinimalSchedule( "FindMe" ), TestContext.CancellationToken );

        Schedule? found = await _service.GetByNameAsync( "FindMe", TestContext.CancellationToken );

        Assert.IsNotNull( found );
        Assert.AreEqual( "FindMe", found!.DbSchedule.Name );
    }

    [TestMethod]
    public async Task GetByNameAsync_NonExistentName_ReturnsNull( ) {
        Schedule? result = await _service.GetByNameAsync( "NoSuchName", TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    #endregion GetByNameAsync

    #region GetAllAsync

    [TestMethod]
    public async Task GetAllAsync_Empty_ReturnsEmptyList( ) {
        IReadOnlyList<Schedule> result = await _service.GetAllAsync( TestContext.CancellationToken );
        Assert.HasCount( 0, result );
    }

    [TestMethod]
    public async Task GetAllAsync_MultipleSchedules_ReturnsAll( ) {
        _ = await _service.CreateAsync( MakeMinimalSchedule( "A" ), TestContext.CancellationToken );
        _ = await _service.CreateAsync( MakeDailySchedule( "B" ), TestContext.CancellationToken );
        _ = await _service.CreateAsync( MakeWeeklySchedule( "C" ), TestContext.CancellationToken );

        IReadOnlyList<Schedule> all = await _service.GetAllAsync( TestContext.CancellationToken );
        Assert.HasCount( 3, all );
    }

    #endregion GetAllAsync

    #region UpdateAsync

    [TestMethod]
    public async Task UpdateAsync_UpdateCoreName_Reflects( ) {
        Schedule created = await _service.CreateAsync( MakeMinimalSchedule( "OldName" ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;

        Schedule update = MakeMinimalSchedule( "NewName" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.AreEqual( "NewName", updated.DbSchedule.Name );
    }

    [TestMethod]
    public async Task UpdateAsync_AddRecurrence_PersistsNewRecurrence( ) {
        Schedule created = await _service.CreateAsync( MakeMinimalSchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;

        Schedule update = MakeDailySchedule( "Test Schedule" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.IsNotNull( updated.DailyRecurrence );
        Assert.AreEqual( 2, updated.DailyRecurrence!.DayInterval );
    }

    [TestMethod]
    public async Task UpdateAsync_ChangeRecurrenceType_RemovesOldAddsNew( ) {
        Schedule daily = await _service.CreateAsync( MakeDailySchedule( ), TestContext.CancellationToken );
        Guid id = daily.DbSchedule.Id;

        Schedule update = MakeWeeklySchedule( "Daily Schedule" );
        update.DbSchedule.Id = id;

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.IsNull( updated.DailyRecurrence );
        Assert.IsNotNull( updated.WeeklyRecurrence );
    }

    [TestMethod]
    public async Task UpdateAsync_AddExpiration_PersistsExpiration( ) {
        Schedule created = await _service.CreateAsync( MakeMinimalSchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;
        Assert.IsNull( created.Expiration );

        Schedule update = MakeMinimalSchedule( );
        update.DbSchedule.Id = id;
        update.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly( 2026, 1, 1 ),
            Time = new TimeOnly( 0, 0 ),
            TimeZone = TimeZoneInfo.Utc,
        };

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.IsNotNull( updated.Expiration );
        Assert.AreEqual( new DateOnly( 2026, 1, 1 ), updated.Expiration!.Date );
    }

    [TestMethod]
    public async Task UpdateAsync_RemoveExpiration_RemovesExpiration( ) {
        Schedule created = await _service.CreateAsync( MakeFullSchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;
        Assert.IsNotNull( created.Expiration );

        // Remove expiration by not including it
        Schedule update = MakeDailySchedule( "Full Schedule" );
        update.DbSchedule.Id = id;
        update.RepeatOptions = new ScheduleRepeatOptions {
            RepeatIntervalMinutes = 60,
            RepeatDurationMinutes = 480,
        };

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.IsNull( updated.Expiration );
    }

    [TestMethod]
    public async Task UpdateAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        Schedule schedule = MakeMinimalSchedule( );
        schedule.DbSchedule.Id = Guid.NewGuid( );

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) =>
            _service.UpdateAsync( schedule, TestContext.CancellationToken ) );
    }

    #endregion UpdateAsync

    #region DeleteAsync

    [TestMethod]
    public async Task DeleteAsync_ExistingSchedule_RemovesAllSubEntities( ) {
        Schedule created = await _service.CreateAsync( MakeFullSchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;

        await _service.DeleteAsync( id, TestContext.CancellationToken );

        Assert.IsNull( await _service.GetByIdAsync( id, TestContext.CancellationToken ) );
        Assert.HasCount( 0, await _dbContext.Schedules.ToListAsync( TestContext.CancellationToken ) );
        Assert.HasCount( 0, await _dbContext.StartDateTimeInfos.ToListAsync( TestContext.CancellationToken ) );
        Assert.HasCount( 0, await _dbContext.ExpirationDateTimeInfos.ToListAsync( TestContext.CancellationToken ) );
        Assert.HasCount( 0, await _dbContext.ScheduleRepeatOptions.ToListAsync( TestContext.CancellationToken ) );
        Assert.HasCount( 0, await _dbContext.DailyRecurrences.ToListAsync( TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task DeleteAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) =>
            _service.DeleteAsync( Guid.NewGuid( ), TestContext.CancellationToken ) );
    }

    #endregion DeleteAsync

    #region PreviewOccurrencesAsync

    [TestMethod]
    public async Task PreviewOccurrencesAsync_DailySchedule_ReturnsOccurrences( ) {
        Schedule created = await _service.CreateAsync( MakeDailySchedule( ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;

        DateTime windowEnd = new( 2025, 7, 15, 23, 59, 59, DateTimeKind.Utc );
        ScheduleOccurrenceResult result = await _service.PreviewOccurrencesAsync(
            id, windowEnd, TestContext.CancellationToken );

        Assert.IsNotEmpty( result.Occurrences );
    }

    [TestMethod]
    public async Task PreviewOccurrencesAsync_NonExistentId_ThrowsKeyNotFoundException( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) =>
            _service.PreviewOccurrencesAsync(
                Guid.NewGuid( ),
                DateTime.UtcNow.AddDays( 30 ),
                TestContext.CancellationToken ) );
    }

    #endregion PreviewOccurrencesAsync

    #region RoundTrip

    [TestMethod]
    public async Task RoundTrip_CreateReadUpdateDelete_Succeeds( ) {
        // Create
        Schedule created = await _service.CreateAsync( MakeDailySchedule( "RoundTrip" ), TestContext.CancellationToken );
        Guid id = created.DbSchedule.Id;
        Assert.AreEqual( "RoundTrip", created.DbSchedule.Name );
        Assert.AreEqual( 2, created.DailyRecurrence!.DayInterval );

        // Read
        Schedule? read = await _service.GetByIdAsync( id, TestContext.CancellationToken );
        Assert.IsNotNull( read );
        Assert.AreEqual( id, read!.DbSchedule.Id );

        // Update — change name + add expiration
        Schedule update = MakeDailySchedule( "RoundTripUpdated" );
        update.DbSchedule.Id = id;
        update.Expiration = new ExpirationDateTimeInfo {
            Date = new DateOnly( 2026, 6, 15 ),
            Time = new TimeOnly( 17, 0 ),
            TimeZone = TimeZoneInfo.Utc,
        };

        Schedule updated = await _service.UpdateAsync( update, TestContext.CancellationToken );
        Assert.AreEqual( "RoundTripUpdated", updated.DbSchedule.Name );
        Assert.IsNotNull( updated.Expiration );

        // Delete
        await _service.DeleteAsync( id, TestContext.CancellationToken );
        Assert.IsNull( await _service.GetByIdAsync( id, TestContext.CancellationToken ) );
    }

    #endregion RoundTrip
}
