using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Scheduling;

/// <summary>
/// Unit tests for <see cref="RetryFromFailedService"/>, validating retry-from-failed orchestration
/// including variable override batching, failed-step validation, and schedule creation.
/// </summary>
[TestClass]
public class RetryFromFailedServiceTests {
    /// <summary>
    /// The in-memory SQLite connection used for database operations.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The SQLite-backed <see cref="WerkrDbContext"/> used for test data persistence.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="RetryFromFailedService"/> instance under test.
    /// </summary>
    private RetryFromFailedService _service = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database, the schema, and the service under test.
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

        _service = new RetryFromFailedService(
            _dbContext,
            NullLogger<RetryFromFailedService>.Instance
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
    /// Seeds a minimal workflow with a single task, step, run, and failed step execution.
    /// Returns a tuple of (workflowId, runId, stepId).
    /// </summary>
    private async Task<(long WorkflowId, Guid RunId, long StepId)> SeedFailedRunAsync(
        CancellationToken ct ) {

        Workflow workflow = new( ) { Name = "Test Workflow", Description = "Test" };
        _ = _dbContext.Set<Workflow>( ).Add( workflow );
        _ = await _dbContext.SaveChangesAsync( ct );

        WerkrTask task = new( ) {
            Name = "Test Task",
            WorkflowId = workflow.Id,
            ActionType = TaskActionType.PowerShellCommand,
            Content = "Write-Output 'test'",
            TargetTags = ["default"],
        };
        _ = _dbContext.Set<WerkrTask>( ).Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowStep step = new( ) {
            WorkflowId = workflow.Id,
            TaskId = task.Id,
            Order = 1,
        };
        _ = _dbContext.WorkflowSteps.Add( step );
        _ = await _dbContext.SaveChangesAsync( ct );

        Guid runId = Guid.NewGuid( );
        WorkflowRun run = new( ) {
            Id = runId,
            WorkflowId = workflow.Id,
            StartTime = DateTime.UtcNow.AddMinutes( -5 ),
            EndTime = DateTime.UtcNow.AddMinutes( -1 ),
            Status = WorkflowRunStatus.Failed,
        };
        _ = _dbContext.WorkflowRuns.Add( run );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowStepExecution failedExecution = new( ) {
            WorkflowRunId = runId,
            StepId = step.Id,
            Attempt = 1,
            Status = StepExecutionStatus.Failed,
        };
        _ = _dbContext.WorkflowStepExecutions.Add( failedExecution );
        _ = await _dbContext.SaveChangesAsync( ct );

        return (workflow.Id, runId, step.Id);
    }

    #endregion

    /// <summary>
    /// Verifies that retry succeeds for a valid failed run and creates a new schedule.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_ValidFailedRun_ReturnsResult( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        RetryFromFailedService.RetryResult result = await _service.RetryAsync(
            workflowId, runId, stepId, null, ct
        );

        Assert.AreEqual( runId, result.RunId );
        Assert.AreEqual( stepId, result.RetryFromStepId );
        Assert.AreEqual( 1, result.ResetStepCount );
    }

    /// <summary>
    /// Verifies that retry transitions the run from Failed to Running.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_TransitionsRunToRunning( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        _ = await _service.RetryAsync( workflowId, runId, stepId, null, ct );

        WorkflowRun run = await _dbContext.WorkflowRuns.AsNoTracking( ).FirstAsync( r => r.Id == runId, ct );

        Assert.AreEqual( WorkflowRunStatus.Running, run.Status );
        Assert.IsNull( run.EndTime );
    }

    /// <summary>
    /// Verifies that retry creates a new Pending step execution with an incremented attempt number.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_CreatesNewPendingExecution( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        _ = await _service.RetryAsync( workflowId, runId, stepId, null, ct );

        List<WorkflowStepExecution> executions = await _dbContext.WorkflowStepExecutions
            .Where( e => e.WorkflowRunId == runId && e.StepId == stepId )
            .OrderBy( e => e.Attempt )
            .ToListAsync( ct );

        Assert.HasCount( 2, executions );
        Assert.AreEqual( StepExecutionStatus.Failed, executions[0].Status );
        Assert.AreEqual( 1, executions[0].Attempt );
        Assert.AreEqual( StepExecutionStatus.Pending, executions[1].Status );
        Assert.AreEqual( 2, executions[1].Attempt );
    }

    /// <summary>
    /// Verifies that variable overrides are persisted with incremented versions using
    /// the batch query (not N+1).
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_WithVariableOverrides_PersistsNewVersions( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        // Seed existing variables (version 1)
        WorkflowRunVariable existingVar1 = new( ) {
            WorkflowRunId = runId,
            VariableName = "Env",
            Value = "staging",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        };
        WorkflowRunVariable existingVar2 = new( ) {
            WorkflowRunId = runId,
            VariableName = "Retries",
            Value = "3",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        };
        _dbContext.Set<WorkflowRunVariable>( ).AddRange( existingVar1, existingVar2 );
        _ = await _dbContext.SaveChangesAsync( ct );

        Dictionary<string, string> overrides = new( ) {
            ["Env"] = "production",
            ["Retries"] = "5",
        };

        _ = await _service.RetryAsync( workflowId, runId, stepId, overrides, ct );

        List<WorkflowRunVariable> envVars = await _dbContext.Set<WorkflowRunVariable>( )
            .Where( v => v.WorkflowRunId == runId && v.VariableName == "Env" )
            .OrderBy( v => v.Version )
            .ToListAsync( ct );

        Assert.HasCount( 2, envVars );
        Assert.AreEqual( 1, envVars[0].Version );
        Assert.AreEqual( "staging", envVars[0].Value );
        Assert.AreEqual( 2, envVars[1].Version );
        Assert.AreEqual( "production", envVars[1].Value );
        Assert.AreEqual( VariableSource.ReExecutionEdit, envVars[1].Source );

        List<WorkflowRunVariable> retriesVars = await _dbContext.Set<WorkflowRunVariable>( )
            .Where( v => v.WorkflowRunId == runId && v.VariableName == "Retries" )
            .OrderBy( v => v.Version )
            .ToListAsync( ct );

        Assert.HasCount( 2, retriesVars );
        Assert.AreEqual( 2, retriesVars[1].Version );
        Assert.AreEqual( "5", retriesVars[1].Value );
    }

    /// <summary>
    /// Verifies that overrides for new variables (no prior version) start at version 1.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_WithNewVariable_StartsAtVersionOne( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        Dictionary<string, string> overrides = new( ) {
            ["NewVar"] = "hello",
        };

        _ = await _service.RetryAsync( workflowId, runId, stepId, overrides, ct );

        WorkflowRunVariable? newVar = await _dbContext.Set<WorkflowRunVariable>( )
            .FirstOrDefaultAsync( v => v.WorkflowRunId == runId && v.VariableName == "NewVar", ct );

        Assert.IsNotNull( newVar );
        Assert.AreEqual( 1, newVar.Version );
        Assert.AreEqual( "hello", newVar.Value );
        Assert.AreEqual( VariableSource.ReExecutionEdit, newVar.Source );
    }

    /// <summary>
    /// Verifies that retrying a run not in Failed status throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_RunNotFailed_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        // Transition the run to Running first
        WorkflowRun run = await _dbContext.WorkflowRuns.FirstAsync( r => r.Id == runId, ct );
        run.Status = WorkflowRunStatus.Running;
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) =>
            _service.RetryAsync( workflowId, runId, stepId, null, ct )
        );
    }

    /// <summary>
    /// Verifies that retrying with a step that doesn't belong to the workflow throws
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_StepNotInWorkflow_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, _) = await SeedFailedRunAsync( ct );

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) =>
            _service.RetryAsync( workflowId, runId, 99999, null, ct )
        );
    }

    /// <summary>
    /// Verifies that retrying from a step without a Failed execution throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_StepNotFailed_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        // Change the execution status to Completed
        WorkflowStepExecution exec = await _dbContext.WorkflowStepExecutions
            .FirstAsync( e => e.WorkflowRunId == runId && e.StepId == stepId, ct );
        exec.Status = StepExecutionStatus.Completed;
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) =>
            _service.RetryAsync( workflowId, runId, stepId, null, ct )
        );
    }

    /// <summary>
    /// Verifies that retry creates a one-time schedule linked to the workflow.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_CreatesOneTimeSchedule( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        RetryFromFailedService.RetryResult result = await _service.RetryAsync(
            workflowId, runId, stepId, null, ct
        );

        WorkflowSchedule? link = await _dbContext.WorkflowSchedules
            .FirstOrDefaultAsync( ws => ws.ScheduleId == result.ScheduleId, ct );

        Assert.IsNotNull( link );
        Assert.AreEqual( workflowId, link.WorkflowId );
        Assert.IsTrue( link.IsOneTime );
        Assert.AreEqual( runId, link.WorkflowRunId );
    }
}
