using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Tests.Data.Unit.Tasks;

/// <summary>
/// Contains unit tests for the <see cref="TaskService"/> class defined in Werkr.Core. Validates CRUD operations,
/// validation rules (name, content, tags, action type, timeout), enable/disable toggling, and error handling for
/// missing entities.
/// </summary>
[TestClass]
public class TaskServiceTests {
    /// <summary>
    /// The in-memory SQLite connection kept open for the duration of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The <see cref="SqliteWerkrDbContext"/> used for seeding and querying test data.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="TaskService"/> instance under test.
    /// </summary>
    private TaskService _service = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database and constructs the <see cref="TaskService"/> under test.
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

        TaskVersionService versionService = new(
            _dbContext,
            new NoopAuditService( ),
            NullLogger<TaskVersionService>.Instance
        );

        NoopAuditService auditService = new( );

        _service = new TaskService(
            _dbContext,
            versionService,
            auditService,
            NullLogger<TaskService>.Instance
        );
    }

    /// <summary>
    /// Disposes the database context and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Creates a <see cref="WerkrTask"/> with default valid values for use in tests.
    /// </summary>
    private static WerkrTask MakeTask( string name = "Test Task" ) => new( ) {
        Name = name,
        ActionType = TaskActionType.ShellCommand,
        Content = "echo hello",
        TargetTags = ["linux"],
    };

    // ── Create ──

    /// <summary>
    /// Verifies that creating a task sets a positive ID and a random sync interval between 30 and 60 minutes.
    /// </summary>
    [TestMethod]
    public async Task Create_SetsIdAndSyncInterval( ) {
        WerkrTask task = MakeTask( );
        WerkrTask created = await _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        );

        Assert.IsGreaterThan(
            0L,
            created.Id
        );
        Assert.IsTrue( created.SyncIntervalMinutes is >= 30 and <= 60 );
    }

    /// <summary>
    /// Verifies that a created task is persisted in the database.
    /// </summary>
    [TestMethod]
    public async Task Create_PersistsInDatabase( ) {
        WerkrTask task = MakeTask( );
        WerkrTask created = await _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        );

        WerkrTask? fromDb = await _dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == created.Id,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( fromDb );
        Assert.AreEqual(
            "Test Task",
            fromDb.Name
        );
    }

    /// <summary>
    /// Verifies that creating a task with an empty name throws <see cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task Create_RequiresName( ) {
        WerkrTask task = MakeTask( );
        task.Name = string.Empty;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that creating a task with empty content throws <see cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task Create_RequiresContent( ) {
        WerkrTask task = MakeTask( );
        task.Content = string.Empty;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that creating a task with empty target tags succeeds.
    /// Tags are optional — the UI warns if empty but the API allows it.
    /// </summary>
    [TestMethod]
    public async Task Create_AllowsEmptyTargetTags( ) {
        WerkrTask task = MakeTask( );
        task.TargetTags = [];

        WerkrTask created = await _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        );

        Assert.IsNotNull( created );
        Assert.IsEmpty( created.TargetTags );
    }

    /// <summary>
    /// Verifies that creating a task with an invalid <see cref="TaskActionType"/> throws <see
    /// cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task Create_RejectsInvalidActionType( ) {
        WerkrTask task = MakeTask( );
        task.ActionType = (TaskActionType)999;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that creating a task with a negative timeout throws <see cref="ValidationException"/>.
    /// </summary>
    [TestMethod]
    public async Task Create_RejectsNegativeTimeout( ) {
        WerkrTask task = MakeTask( );
        task.TimeoutMinutes = -5;

        _ = await Assert.ThrowsExactlyAsync<ValidationException>( ( ) => _service.CreateAsync(
            task,
            ct: TestContext.CancellationToken
        ) );
    }

    // ── GetAll ──

    /// <summary>
    /// Verifies that <see cref="GetAllAsync"/> returns an empty list when no tasks have been created.
    /// </summary>
    [TestMethod]
    public async Task GetAll_ReturnsEmpty_WhenNoTasks( ) {
        IReadOnlyList<WerkrTask> tasks = await _service.GetAllAsync( ct: TestContext.CancellationToken );
        Assert.IsEmpty( tasks );
    }

    /// <summary>
    /// Verifies that <see cref="GetAllAsync"/> returns all previously created tasks.
    /// </summary>
    [TestMethod]
    public async Task GetAll_ReturnsCreatedTasks( ) {
        _ = await _service.CreateAsync(
            MakeTask( "A" ),
            ct: TestContext.CancellationToken
        );
        _ = await _service.CreateAsync(
            MakeTask( "B" ),
            ct: TestContext.CancellationToken
        );

        IReadOnlyList<WerkrTask> tasks = await _service.GetAllAsync( ct: TestContext.CancellationToken );
        Assert.HasCount(
            2,
            tasks
        );
    }

    // ── GetById ──

    /// <summary>
    /// Verifies that <see cref="GetByIdAsync"/> returns the correct task.
    /// </summary>
    [TestMethod]
    public async Task GetById_ReturnsTask( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );

        WerkrTask? found = await _service.GetByIdAsync(
            created.Id,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( found );
        Assert.AreEqual(
            created.Id,
            found.Id
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetByIdAsync"/> returns <see langword="null"/> when the task does not exist.
    /// </summary>
    [TestMethod]
    public async Task GetById_ReturnsNull_WhenNotFound( ) {
        WerkrTask? found = await _service.GetByIdAsync(
            999,
            TestContext.CancellationToken
        );
        Assert.IsNull( found );
    }

    // ── Update ──

    /// <summary>
    /// Verifies that <see cref="UpdateAsync"/> changes the task name.
    /// </summary>
    [TestMethod]
    public async Task Update_ChangesName( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );

        WerkrTask update = MakeTask( "Updated Name" );
        update.Id = created.Id;
        WerkrTask updated = await _service.UpdateAsync(
            update,
            ct: TestContext.CancellationToken
        );

        Assert.AreEqual(
            "Updated Name",
            updated.Name
        );
    }

    /// <summary>
    /// Verifies that <see cref="UpdateAsync"/> throws <see cref="KeyNotFoundException"/> when the task does not exist.
    /// </summary>
    [TestMethod]
    public async Task Update_ThrowsKeyNotFound_WhenMissing( ) {
        WerkrTask update = MakeTask( );
        update.Id = 999;

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.UpdateAsync(
            update,
            ct: TestContext.CancellationToken
        ) );
    }

    // ── Delete ──

    /// <summary>
    /// Verifies that <see cref="DeleteAsync"/> removes the task from the database.
    /// </summary>
    [TestMethod]
    public async Task Delete_RemovesTask( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );
        await _service.DeleteAsync(
            created.Id,
            ct: TestContext.CancellationToken
        );

        WerkrTask? found = await _service.GetByIdAsync(
            created.Id,
            TestContext.CancellationToken
        );
        Assert.IsNull( found );
    }

    /// <summary>
    /// Verifies that <see cref="DeleteAsync"/> throws <see cref="KeyNotFoundException"/> when the task does not exist.
    /// </summary>
    [TestMethod]
    public async Task Delete_ThrowsKeyNotFound_WhenMissing( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.DeleteAsync(
            999,
            ct: TestContext.CancellationToken
        ) );
    }

    // ── SetEnabled ──

    /// <summary>
    /// Verifies that <see cref="SetEnabledAsync"/> toggles the task's enabled state.
    /// </summary>
    [TestMethod]
    public async Task SetEnabled_TogglesState( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );
        Assert.IsTrue( created.Enabled );

        await _service.SetEnabledAsync(
            created.Id,
            false,
            ct: TestContext.CancellationToken
        );

        WerkrTask? found = await _dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == created.Id,
            TestContext.CancellationToken
        );
        Assert.IsNotNull( found );
        Assert.IsFalse( found.Enabled );
    }

    /// <summary>
    /// Verifies that <see cref="SetEnabledAsync"/> throws <see cref="KeyNotFoundException"/> when the task does not
    /// exist.
    /// </summary>
    [TestMethod]
    public async Task SetEnabled_ThrowsKeyNotFound_WhenMissing( ) {
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.SetEnabledAsync(
            999,
            false,
            ct: TestContext.CancellationToken
        ) );
    }

    // ── Versioning ──

    /// <summary>
    /// Verifies that creating a task sets <see cref="WerkrTask.CurrentVersionId"/> and creates version 1.
    /// </summary>
    [TestMethod]
    public async Task Create_SetsCurrentVersionId( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );

        WerkrTask? fromDb = await _dbContext.Tasks
            .Include( t => t.CurrentVersion )
            .FirstOrDefaultAsync( t => t.Id == created.Id, TestContext.CancellationToken );

        Assert.IsNotNull( fromDb );
        Assert.IsNotNull( fromDb.CurrentVersionId );
        Assert.IsNotNull( fromDb.CurrentVersion );
        Assert.AreEqual( 1, fromDb.CurrentVersion.VersionNumber );
    }

    /// <summary>
    /// Verifies that updating a task increments the version number.
    /// </summary>
    [TestMethod]
    public async Task Update_IncrementsVersionNumber( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );

        WerkrTask update = MakeTask( "Updated" );
        update.Id = created.Id;
        _ = await _service.UpdateAsync(
            update,
            changeDescription: "Test update",
            ct: TestContext.CancellationToken
        );

        WerkrTask? fromDb = await _dbContext.Tasks
            .Include( t => t.CurrentVersion )
            .FirstOrDefaultAsync( t => t.Id == created.Id, TestContext.CancellationToken );

        Assert.IsNotNull( fromDb?.CurrentVersion );
        Assert.AreEqual( 2, fromDb.CurrentVersion.VersionNumber );
    }

    /// <summary>
    /// Verifies that toggling enabled creates a new version.
    /// </summary>
    [TestMethod]
    public async Task SetEnabled_CreatesVersion( ) {
        WerkrTask created = await _service.CreateAsync(
            MakeTask( ),
            ct: TestContext.CancellationToken
        );

        await _service.SetEnabledAsync(
            created.Id,
            false,
            ct: TestContext.CancellationToken
        );

        WerkrTask? fromDb = await _dbContext.Tasks
            .Include( t => t.CurrentVersion )
            .FirstOrDefaultAsync( t => t.Id == created.Id, TestContext.CancellationToken );

        Assert.IsNotNull( fromDb?.CurrentVersion );
        Assert.AreEqual( 2, fromDb.CurrentVersion.VersionNumber );
    }

    /// <summary>No-op audit service for unit tests that don't need audit logging.</summary>
    private sealed class NoopAuditService : IAuditService {
        public Task LogAsync( AuditEntry entry, CancellationToken ct = default ) => Task.CompletedTask;
        public Task<PagedResult<AuditEventDto>> QueryAsync( AuditQuery query, CancellationToken ct = default ) =>
            Task.FromResult( new PagedResult<AuditEventDto>( [], 0, 25, 0 ) );
        public Task ExportAsync( AuditQuery query, ExportFormat format, Stream outputStream, CancellationToken ct = default, int? maxRows = null ) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetEntityTypesAsync( CancellationToken ct = default ) => throw new NotImplementedException( );
    }
}
