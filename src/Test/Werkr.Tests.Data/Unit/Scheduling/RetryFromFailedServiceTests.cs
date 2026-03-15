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
    /// Verifies that retrying with a mismatched workflowId (run belongs to a different workflow)
    /// throws <see cref="InvalidOperationException"/> without modifying the run status.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_WorkflowIdMismatch_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (_, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        long wrongWorkflowId = 99999;
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) =>
            _service.RetryAsync( wrongWorkflowId, runId, stepId, null, ct )
        );

        // Verify the run status was NOT changed.
        WorkflowRun run = await _dbContext.WorkflowRuns.FirstAsync(r => r.Id == runId, ct);
        Assert.AreEqual( WorkflowRunStatus.Failed, run.Status );
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

    #region DAG and Repeated-Retry Tests

    /// <summary>
    /// Seeds a 3-step DAG: A → B → C with B in Failed status and A/C in Completed/Pending.
    /// Returns (workflowId, runId, stepAId, stepBId, stepCId).
    /// </summary>
    private async Task<(long WorkflowId, Guid RunId, long StepAId, long StepBId, long StepCId)> SeedDagFailedRunAsync(
        CancellationToken ct ) {

        Workflow workflow = new() { Name = "DAG Workflow", Description = "A→B→C" };
        _ = _dbContext.Set<Workflow>( ).Add( workflow );
        _ = await _dbContext.SaveChangesAsync( ct );

        WerkrTask taskA = new()
        {
            Name = "Task A",
            WorkflowId = workflow.Id,
            ActionType = TaskActionType.PowerShellCommand,
            Content = "echo A",
            TargetTags = ["default"],
        };
        WerkrTask taskB = new()
        {
            Name = "Task B",
            WorkflowId = workflow.Id,
            ActionType = TaskActionType.PowerShellCommand,
            Content = "echo B",
            TargetTags = ["default"],
        };
        WerkrTask taskC = new()
        {
            Name = "Task C",
            WorkflowId = workflow.Id,
            ActionType = TaskActionType.PowerShellCommand,
            Content = "echo C",
            TargetTags = ["default"],
        };
        _dbContext.Set<WerkrTask>( ).AddRange( taskA, taskB, taskC );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowStep stepA = new() { WorkflowId = workflow.Id, TaskId = taskA.Id, Order = 1 };
        WorkflowStep stepB = new() { WorkflowId = workflow.Id, TaskId = taskB.Id, Order = 2 };
        WorkflowStep stepC = new() { WorkflowId = workflow.Id, TaskId = taskC.Id, Order = 3 };
        _dbContext.WorkflowSteps.AddRange( stepA, stepB, stepC );
        _ = await _dbContext.SaveChangesAsync( ct );

        // Dependencies: B depends on A, C depends on B
        _dbContext.WorkflowStepDependencies.AddRange(
            new WorkflowStepDependency { StepId = stepB.Id, DependsOnStepId = stepA.Id },
            new WorkflowStepDependency { StepId = stepC.Id, DependsOnStepId = stepB.Id }
        );
        _ = await _dbContext.SaveChangesAsync( ct );

        Guid runId = Guid.NewGuid();
        WorkflowRun run = new()
        {
            Id = runId,
            WorkflowId = workflow.Id,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddMinutes(-1),
            Status = WorkflowRunStatus.Failed,
        };
        _ = _dbContext.WorkflowRuns.Add( run );
        _ = await _dbContext.SaveChangesAsync( ct );

        // A = Completed (attempt 1), B = Failed (attempt 1), C = Pending (attempt 1)
        _dbContext.WorkflowStepExecutions.AddRange(
            new WorkflowStepExecution { WorkflowRunId = runId, StepId = stepA.Id, Attempt = 1, Status = StepExecutionStatus.Completed },
            new WorkflowStepExecution { WorkflowRunId = runId, StepId = stepB.Id, Attempt = 1, Status = StepExecutionStatus.Failed },
            new WorkflowStepExecution { WorkflowRunId = runId, StepId = stepC.Id, Attempt = 1, Status = StepExecutionStatus.Pending }
        );
        _ = await _dbContext.SaveChangesAsync( ct );

        return (workflow.Id, runId, stepA.Id, stepB.Id, stepC.Id);
    }

    /// <summary>
    /// Verifies that retrying from step B in a DAG (A→B→C) resets B and C but not A.
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_DagDownstreamReset_ResetsOnlyTargetAndDownstream( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepAId, long stepBId, long stepCId) =
            await SeedDagFailedRunAsync( ct );

        RetryFromFailedService.RetryResult result = await _service.RetryAsync(
            workflowId, runId, stepBId, null, ct
        );

        // B and C should be reset (2 steps)
        Assert.AreEqual( 2, result.ResetStepCount );

        // Step A should NOT have a new execution — still just its original Completed one
        List<WorkflowStepExecution> execA = await _dbContext.WorkflowStepExecutions
            .Where(e => e.WorkflowRunId == runId && e.StepId == stepAId)
            .ToListAsync(ct);
        Assert.HasCount( 1, execA );
        Assert.AreEqual( StepExecutionStatus.Completed, execA[0].Status );

        // Step B should have attempt 1 (Failed) + attempt 2 (Pending)
        List<WorkflowStepExecution> execB = await _dbContext.WorkflowStepExecutions
            .Where(e => e.WorkflowRunId == runId && e.StepId == stepBId)
            .OrderBy(e => e.Attempt)
            .ToListAsync(ct);
        Assert.HasCount( 2, execB );
        Assert.AreEqual( StepExecutionStatus.Failed, execB[0].Status );
        Assert.AreEqual( 1, execB[0].Attempt );
        Assert.AreEqual( StepExecutionStatus.Pending, execB[1].Status );
        Assert.AreEqual( 2, execB[1].Attempt );

        // Step C should have attempt 1 (Pending, original) + attempt 2 (Pending, retry)
        List<WorkflowStepExecution> execC = await _dbContext.WorkflowStepExecutions
            .Where(e => e.WorkflowRunId == runId && e.StepId == stepCId)
            .OrderBy(e => e.Attempt)
            .ToListAsync(ct);
        Assert.HasCount( 2, execC );
        Assert.AreEqual( 1, execC[0].Attempt );
        Assert.AreEqual( StepExecutionStatus.Pending, execC[1].Status );
        Assert.AreEqual( 2, execC[1].Attempt );
    }

    /// <summary>
    /// Verifies that retrying twice increments attempt numbers correctly (1→2→3).
    /// </summary>
    [TestMethod]
    public async Task RetryAsync_RepeatedRetry_IncrementsAttempts( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (long workflowId, Guid runId, long stepId) = await SeedFailedRunAsync( ct );

        // First retry: attempt 1 (Failed) → creates attempt 2 (Pending)
        _ = await _service.RetryAsync( workflowId, runId, stepId, null, ct );

        // Simulate the retried execution failing again using ExecuteUpdateAsync
        // (bypasses the change tracker, same as the service's CAS pattern).
        _dbContext.ChangeTracker.Clear( );

        _ = await _dbContext.WorkflowStepExecutions
            .Where( e => e.WorkflowRunId == runId && e.StepId == stepId && e.Attempt == 2 )
            .ExecuteUpdateAsync( s => s.SetProperty( e => e.Status, StepExecutionStatus.Failed ), ct );

        _ = await _dbContext.WorkflowRuns
            .Where( r => r.Id == runId )
            .ExecuteUpdateAsync( s => s
                .SetProperty( r => r.Status, WorkflowRunStatus.Failed )
                .SetProperty( r => r.EndTime, DateTime.UtcNow ), ct );

        // Second retry: should create attempt 3
        _ = await _service.RetryAsync( workflowId, runId, stepId, null, ct );

        _dbContext.ChangeTracker.Clear( );

        List<WorkflowStepExecution> allExecs = await _dbContext.WorkflowStepExecutions
            .Where(e => e.WorkflowRunId == runId && e.StepId == stepId)
            .OrderBy(e => e.Attempt)
            .ToListAsync(ct);

        Assert.HasCount( 3, allExecs );
        Assert.AreEqual( 1, allExecs[0].Attempt );
        Assert.AreEqual( StepExecutionStatus.Failed, allExecs[0].Status );
        Assert.AreEqual( 2, allExecs[1].Attempt );
        Assert.AreEqual( StepExecutionStatus.Failed, allExecs[1].Status );
        Assert.AreEqual( 3, allExecs[2].Attempt );
        Assert.AreEqual( StepExecutionStatus.Pending, allExecs[2].Status );
    }

    #endregion
}
