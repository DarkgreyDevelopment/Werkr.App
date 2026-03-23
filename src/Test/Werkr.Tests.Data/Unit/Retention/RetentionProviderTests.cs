using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Werkr.Core.Retention;
using Werkr.Core.Retention.Providers;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Retention;

/// <summary>
/// Unit tests for retention providers: <see cref="WorkflowRunRetentionProvider"/>,
/// <see cref="AuditLogRetentionProvider"/>, <see cref="JobOutputRetentionProvider"/>,
/// and <see cref="WorkflowRunVariableRetentionProvider"/>.
/// </summary>
[TestClass]
public class RetentionProviderTests {

    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _db = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _db = new SqliteWerkrDbContext( options );
        _ = _db.Database.EnsureCreated( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _db.Dispose( );
        _connection.Dispose( );
    }

    // ── WorkflowRunRetentionProvider ──

    [TestMethod]
    public async Task WorkflowRun_Deletes_TerminalRuns_PastRetention( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddDays( -200 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Failed, DateTime.UtcNow.AddDays( -200 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Cancelled, DateTime.UtcNow.AddDays( -200 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddDays( -10 ) ); // within retention

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 3, result.DeletedCount );

        int remaining = await _db.WorkflowRuns.CountAsync( ct );
        Assert.AreEqual( 1, remaining );
    }

    [TestMethod]
    public async Task WorkflowRun_Exempts_Running( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Running, null, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    [TestMethod]
    public async Task WorkflowRun_Exempts_Pending( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Pending, null, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    [TestMethod]
    public async Task WorkflowRun_Exempts_Queued( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Queued, null, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    [TestMethod]
    public async Task WorkflowRun_Exempts_Paused( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Paused, null, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    [TestMethod]
    public async Task WorkflowRun_RetentionZero_DeletesAllTerminal( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddMinutes( -1 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Running, null, DateTime.UtcNow.AddDays( -1 ) ); // exempt

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 0, 1000, ct );
        Assert.AreEqual( 1, result.DeletedCount );
    }

    [TestMethod]
    public async Task WorkflowRun_Preview_ReturnsAccurateCountWithoutDeleting( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunRetentionProvider provider = new( _db );

        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddDays( -200 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Failed, DateTime.UtcNow.AddDays( -200 ) );
        _ = await SeedWorkflowRunAsync( WorkflowRunStatus.Running, null, DateTime.UtcNow.AddDays( -200 ) ); // exempt

        RetentionPreview preview = await provider.PreviewAgedRecordsAsync( 180, ct );
        Assert.AreEqual( 2, preview.EligibleCount );

        // Verify nothing was actually deleted
        int totalRuns = await _db.WorkflowRuns.CountAsync( ct );
        Assert.AreEqual( 3, totalRuns );
    }

    // ── JobOutputRetentionProvider ──

    [TestMethod]
    public async Task JobOutput_Deletes_CompletedJobs_PastRetention( ) {
        CancellationToken ct = TestContext.CancellationToken;
        JobOutputRetentionProvider provider = new( _db );

        // Standalone job (no workflow run)
        await SeedJobAsync( null, DateTime.UtcNow.AddDays( -200 ) );
        await SeedJobAsync( null, DateTime.UtcNow.AddDays( -10 ) ); // within retention

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 1, result.DeletedCount );
    }

    [TestMethod]
    public async Task JobOutput_ExemptsJobs_FromActiveWorkflowRuns( ) {
        CancellationToken ct = TestContext.CancellationToken;
        JobOutputRetentionProvider provider = new( _db );

        WorkflowRun run = await SeedWorkflowRunAsync( WorkflowRunStatus.Running, null, DateTime.UtcNow.AddDays( -200 ) );
        await SeedJobAsync( run.Id, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    [TestMethod]
    public async Task JobOutput_DeletesJobs_FromTerminalWorkflowRuns( ) {
        CancellationToken ct = TestContext.CancellationToken;
        JobOutputRetentionProvider provider = new( _db );

        WorkflowRun run = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddDays( -200 ) );
        await SeedJobAsync( run.Id, DateTime.UtcNow.AddDays( -200 ) );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 1, result.DeletedCount );
    }

    // ── WorkflowRunVariableRetentionProvider ──

    [TestMethod]
    public async Task VariableVersion_Deletes_FromTerminalRuns_PastRetention( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunVariableRetentionProvider provider = new( _db );

        WorkflowRun run = await SeedWorkflowRunAsync( WorkflowRunStatus.Succeeded, DateTime.UtcNow.AddDays( -200 ) );
        await SeedWorkflowRunVariableAsync( run.Id );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 1, result.DeletedCount );
    }

    [TestMethod]
    public async Task VariableVersion_Exempts_FromActiveRuns( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRunVariableRetentionProvider provider = new( _db );

        WorkflowRun run = await SeedWorkflowRunAsync( WorkflowRunStatus.Running, null, DateTime.UtcNow.AddDays( -200 ) );
        await SeedWorkflowRunVariableAsync( run.Id );

        RetentionSweepResult result = await provider.DeleteAgedRecordsAsync( 180, 1000, ct );
        Assert.AreEqual( 0, result.DeletedCount );
    }

    // ── RetentionPolicyRegistry ──

    [TestMethod]
    public void Registry_Register_RetrieveProvider( ) {
        RetentionPolicyRegistry registry = new( );
        WorkflowRunRetentionProvider provider = new( _db );

        registry.Register( provider );

        IRetentionPolicyProvider? retrieved = registry.GetProvider( "workflow_run" );
        Assert.IsNotNull( retrieved );
        Assert.AreEqual( "workflow_run", retrieved.EntityType );
    }

    [TestMethod]
    public void Registry_DuplicateRegistration_Throws( ) {
        RetentionPolicyRegistry registry = new( );
        WorkflowRunRetentionProvider provider = new( _db );

        registry.Register( provider );
        _ = Assert.ThrowsExactly<ArgumentException>( ( ) => registry.Register( provider ) );
    }

    [TestMethod]
    public void Registry_GetProvider_ReturnsNullForUnknown( ) {
        RetentionPolicyRegistry registry = new( );
        IRetentionPolicyProvider? result = registry.GetProvider( "nonexistent" );
        Assert.IsNull( result );
    }

    // ── Helpers ──

    private async Task<WorkflowRun> SeedWorkflowRunAsync(
        WorkflowRunStatus status, DateTime? endTime, DateTime? startTime = null
    ) {
        // Need a workflow first (FK)
        Workflow wf = new( ) { Name = $"wf-{Guid.NewGuid( ):N}", Enabled = true };
        _ = _db.Workflows.Add( wf );
        _ = await _db.SaveChangesAsync( TestContext.CancellationToken );

        WorkflowRun run = new( ) {
            WorkflowId = wf.Id,
            Status = status,
            StartTime = startTime ?? DateTime.UtcNow.AddDays( -210 ),
            EndTime = endTime,
        };
        _ = _db.WorkflowRuns.Add( run );
        _ = await _db.SaveChangesAsync( TestContext.CancellationToken );
        return run;
    }

    private async Task SeedJobAsync( Guid? workflowRunId, DateTime endTime ) {
        // Need a task first (FK)
        WerkrTask task = new( ) { Name = $"task-{Guid.NewGuid( ):N}", ActionType = TaskActionType.PowerShellCommand };
        _ = _db.Tasks.Add( task );
        _ = await _db.SaveChangesAsync( TestContext.CancellationToken );

        WerkrJob job = new( ) {
            TaskId = task.Id,
            WorkflowRunId = workflowRunId,
            StartTime = endTime.AddMinutes( -5 ),
            EndTime = endTime,
            Success = true,
        };
        _ = _db.Jobs.Add( job );
        _ = await _db.SaveChangesAsync( TestContext.CancellationToken );
    }

    private async Task SeedWorkflowRunVariableAsync( Guid runId ) {
        WorkflowRunVariable variable = new( ) {
            WorkflowRunId = runId,
            VariableName = $"var-{Guid.NewGuid( ):N}",
            Value = "test-value",
            Version = 1,
        };
        _ = _db.WorkflowRunVariables.Add( variable );
        _ = await _db.SaveChangesAsync( TestContext.CancellationToken );
    }
}
