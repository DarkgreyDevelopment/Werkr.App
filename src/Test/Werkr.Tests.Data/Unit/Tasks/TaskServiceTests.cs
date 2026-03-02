using System.ComponentModel.DataAnnotations;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Tests.Data.Unit.Tasks;

[TestClass]
public class TaskServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private TaskService _service = null!;

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

        _service = new TaskService( _dbContext, NullLogger<TaskService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    private static WerkrTask MakeTask( string name = "Test Task" ) => new( ) {
        Name = name,
        ActionType = TaskActionType.ShellCommand,
        Content = "echo hello",
        TargetTags = ["linux"],
    };

    // ── Create ──

    [TestMethod]
    public async Task Create_SetsIdAndSyncInterval( ) {
        WerkrTask task = MakeTask( );
        WerkrTask created = await _service.CreateAsync( task, TestContext.CancellationToken );

        Assert.IsGreaterThan( 0L, created.Id );
        Assert.IsTrue( created.SyncIntervalMinutes is >= 30 and <= 60 );
    }

    [TestMethod]
    public async Task Create_PersistsInDatabase( ) {
        WerkrTask task = MakeTask( );
        WerkrTask created = await _service.CreateAsync( task, TestContext.CancellationToken );

        WerkrTask? fromDb = await _dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == created.Id, TestContext.CancellationToken );
        Assert.IsNotNull( fromDb );
        Assert.AreEqual( "Test Task", fromDb.Name );
    }

    [TestMethod]
    public async Task Create_RequiresName( ) {
        WerkrTask task = MakeTask( );
        task.Name = "";

        _ = await Assert.ThrowsExactlyAsync<ValidationException>(
            ( ) => _service.CreateAsync( task, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task Create_RequiresContent( ) {
        WerkrTask task = MakeTask( );
        task.Content = "";

        _ = await Assert.ThrowsExactlyAsync<ValidationException>(
            ( ) => _service.CreateAsync( task, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task Create_RequiresTargetTags( ) {
        WerkrTask task = MakeTask( );
        task.TargetTags = [];

        _ = await Assert.ThrowsExactlyAsync<ValidationException>(
            ( ) => _service.CreateAsync( task, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task Create_RejectsInvalidActionType( ) {
        WerkrTask task = MakeTask( );
        task.ActionType = (TaskActionType)999;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>(
            ( ) => _service.CreateAsync( task, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task Create_RejectsNegativeTimeout( ) {
        WerkrTask task = MakeTask( );
        task.TimeoutMinutes = -5;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>(
            ( ) => _service.CreateAsync( task, TestContext.CancellationToken ) );
    }

    // ── GetAll ──

    [TestMethod]
    public async Task GetAll_ReturnsEmpty_WhenNoTasks( ) {
        IReadOnlyList<WerkrTask> tasks = await _service.GetAllAsync( ct: TestContext.CancellationToken );
        Assert.IsEmpty( tasks );
    }

    [TestMethod]
    public async Task GetAll_ReturnsCreatedTasks( ) {
        _ = await _service.CreateAsync( MakeTask( "A" ), TestContext.CancellationToken );
        _ = await _service.CreateAsync( MakeTask( "B" ), TestContext.CancellationToken );

        IReadOnlyList<WerkrTask> tasks = await _service.GetAllAsync( ct: TestContext.CancellationToken );
        Assert.HasCount( 2, tasks );
    }

    // ── GetById ──

    [TestMethod]
    public async Task GetById_ReturnsTask( ) {
        WerkrTask created = await _service.CreateAsync( MakeTask( ), TestContext.CancellationToken );

        WerkrTask? found = await _service.GetByIdAsync( created.Id, TestContext.CancellationToken );
        Assert.IsNotNull( found );
        Assert.AreEqual( created.Id, found.Id );
    }

    [TestMethod]
    public async Task GetById_ReturnsNull_WhenNotFound( ) {
        WerkrTask? found = await _service.GetByIdAsync( 999, TestContext.CancellationToken );
        Assert.IsNull( found );
    }

    // ── Update ──

    [TestMethod]
    public async Task Update_ChangesName( ) {
        WerkrTask created = await _service.CreateAsync( MakeTask( ), TestContext.CancellationToken );

        WerkrTask update = MakeTask( "Updated Name" );
        update.Id = created.Id;
        WerkrTask updated = await _service.UpdateAsync( update, TestContext.CancellationToken );

        Assert.AreEqual( "Updated Name", updated.Name );
    }

    [TestMethod]
    public async Task Update_ThrowsKeyNotFound_WhenMissing( ) {
        WerkrTask update = MakeTask( );
        update.Id = 999;

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.UpdateAsync( update, TestContext.CancellationToken ) );
    }

    // ── Delete ──

    [TestMethod]
    public async Task Delete_RemovesTask( ) {
        WerkrTask created = await _service.CreateAsync( MakeTask( ), TestContext.CancellationToken );
        await _service.DeleteAsync( created.Id, TestContext.CancellationToken );

        WerkrTask? found = await _service.GetByIdAsync( created.Id, TestContext.CancellationToken );
        Assert.IsNull( found );
    }

    [TestMethod]
    public async Task Delete_ThrowsKeyNotFound_WhenMissing( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.DeleteAsync( 999, TestContext.CancellationToken ) );
    }

    // ── SetEnabled ──

    [TestMethod]
    public async Task SetEnabled_TogglesState( ) {
        WerkrTask created = await _service.CreateAsync( MakeTask( ), TestContext.CancellationToken );
        Assert.IsTrue( created.Enabled );

        await _service.SetEnabledAsync( created.Id, false, TestContext.CancellationToken );

        WerkrTask? found = await _dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == created.Id, TestContext.CancellationToken );
        Assert.IsNotNull( found );
        Assert.IsFalse( found.Enabled );
    }

    [TestMethod]
    public async Task SetEnabled_ThrowsKeyNotFound_WhenMissing( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.SetEnabledAsync( 999, false, TestContext.CancellationToken ) );
    }
}
