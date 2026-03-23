using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

/// <summary>
/// Unit tests for workflow variable entities, including design-time <see cref="WorkflowVariable"/>
/// definitions, runtime <see cref="WorkflowRunVariable"/> append-only versioning, and
/// <see cref="VariableSource"/> enum usage.
/// </summary>
[TestClass]
public class WorkflowVariableTests {

    /// <summary>In-memory SQLite connection kept open for the lifetime of the test.</summary>
    private SqliteConnection _connection = null!;
    /// <summary>The test database context.</summary>
    private SqliteWerkrDbContext _dbContext = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Opens an in-memory SQLite database and creates the schema.
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
    }

    /// <summary>Disposes the context and connection after each test.</summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // ── WorkflowVariable Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a variable can be created and retrieved with all properties intact.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowVariable_CreateAndRetrieve_RoundTrips( ) {
        long workflowId = await SeedWorkflowAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowVariable variable = new( ) {
            WorkflowId = workflowId,
            Name = "test_var",
            Description = "A test variable",
            DefaultValue = "{\"key\": \"value\"}",
        };
        _ = _dbContext.WorkflowVariables.Add( variable );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowVariable loaded = await _dbContext.WorkflowVariables
            .FirstAsync( v => v.Id == variable.Id, ct );

        Assert.AreEqual( "test_var", loaded.Name );
        Assert.AreEqual( "A test variable", loaded.Description );
        Assert.AreEqual( "{\"key\": \"value\"}", loaded.DefaultValue );
        Assert.AreEqual( workflowId, loaded.WorkflowId );
    }

    /// <summary>
    /// Verifies that two variables with the same name on the same workflow violate
    /// the unique index and cause a <see cref="DbUpdateException"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowVariable_DuplicateName_ThrowsDbUpdateException( ) {
        long workflowId = await SeedWorkflowAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowVariables.Add( new WorkflowVariable {
            WorkflowId = workflowId,
            Name = "dup_var",
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = _dbContext.WorkflowVariables.Add( new WorkflowVariable {
            WorkflowId = workflowId,
            Name = "dup_var",
        } );

        _ = await Assert.ThrowsExactlyAsync<DbUpdateException>(
            ( ) => _dbContext.SaveChangesAsync( ct ) );
    }

    /// <summary>
    /// Verifies that variables with the same name on different workflows are allowed.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowVariable_SameNameDifferentWorkflows_Allowed( ) {
        CancellationToken ct = TestContext.CancellationToken;
        long wf1 = await SeedWorkflowAsync( ct );
        long wf2 = await SeedWorkflowAsync( ct );

        _ = _dbContext.WorkflowVariables.Add( new WorkflowVariable {
            WorkflowId = wf1,
            Name = "shared_name",
        } );
        _ = _dbContext.WorkflowVariables.Add( new WorkflowVariable {
            WorkflowId = wf2,
            Name = "shared_name",
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.WorkflowVariables
            .CountAsync( v => v.Name == "shared_name", ct );
        Assert.AreEqual( 2, count );
    }

    /// <summary>
    /// Verifies that a null default value is acceptable (optional).
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowVariable_NullDefaultValue_Accepted( ) {
        long workflowId = await SeedWorkflowAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowVariable variable = new( ) {
            WorkflowId = workflowId,
            Name = "no_default",
            DefaultValue = null,
        };
        _ = _dbContext.WorkflowVariables.Add( variable );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowVariable loaded = await _dbContext.WorkflowVariables
            .FirstAsync( v => v.Id == variable.Id, ct );
        Assert.IsNull( loaded.DefaultValue );
    }

    /// <summary>
    /// Verifies that deleting a workflow cascades to its variables.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowVariable_CascadeDeleteWithWorkflow( ) {
        long workflowId = await SeedWorkflowAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowVariables.Add( new WorkflowVariable {
            WorkflowId = workflowId,
            Name = "cascade_test",
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        Workflow workflow = await _dbContext.Set<Workflow>( )
            .FirstAsync( w => w.Id == workflowId, ct );
        _ = _dbContext.Set<Workflow>( ).Remove( workflow );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.WorkflowVariables.CountAsync( ct );
        Assert.AreEqual( 0, count );
    }

    // ── WorkflowRunVariable Tests ───────────────────────────────────────────────

    /// <summary>
    /// Verifies that append-only inserts produce correct version numbering and
    /// that the latest value is the highest version.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_AppendOnly_VersionIncrementsCorrectly( ) {
        (long workflowId, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        // Insert three versions
        for (int v = 1; v <= 3; v++) {
            _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
                WorkflowRunId = runId,
                VariableName = "counter",
                Value = $"{{\"n\": {v}}}",
                Version = v,
                Source = VariableSource.StepOutput,
                Created = DateTime.UtcNow,
            } );
        }
        _ = await _dbContext.SaveChangesAsync( ct );

        // Verify 3 rows exist
        List<WorkflowRunVariable> rows = await _dbContext.WorkflowRunVariables
            .Where( r => r.WorkflowRunId == runId && r.VariableName == "counter" )
            .OrderBy( r => r.Version )
            .ToListAsync( ct );

        Assert.HasCount( 3, rows );
        Assert.AreEqual( 1, rows[0].Version );
        Assert.AreEqual( 3, rows[2].Version );

        // Latest = highest version
        WorkflowRunVariable? latest = await _dbContext.WorkflowRunVariables
            .Where( r => r.WorkflowRunId == runId && r.VariableName == "counter" )
            .OrderByDescending( r => r.Version )
            .FirstOrDefaultAsync( ct );

        Assert.IsNotNull( latest );
        Assert.AreEqual( 3, latest.Version );
        Assert.AreEqual( "{\"n\": 3}", latest.Value );
    }

    /// <summary>
    /// Verifies that duplicate (RunId, VariableName, Version) tuples violate the
    /// unique index and cause a <see cref="DbUpdateException"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_DuplicateVersion_ThrowsDbUpdateException( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "v",
            Value = "1",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "v",
            Value = "2",
            Version = 1,
            Source = VariableSource.StepOutput,
            Created = DateTime.UtcNow,
        } );

        _ = await Assert.ThrowsExactlyAsync<DbUpdateException>(
            ( ) => _dbContext.SaveChangesAsync( ct ) );
    }

    /// <summary>
    /// Verifies that the Default VariableSource value is stored correctly.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_DefaultSource_RoundTrips( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "src_test",
            Value = "\"hello\"",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowRunVariable loaded = await _dbContext.WorkflowRunVariables
            .FirstAsync( r => r.VariableName == "src_test", ct );
        Assert.AreEqual( VariableSource.Default, loaded.Source );
    }

    /// <summary>
    /// Verifies that the ManualInput VariableSource value round-trips correctly.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_ManualInputSource_RoundTrips( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "src_manual",
            Value = "\"triggered\"",
            Version = 1,
            Source = VariableSource.ManualInput,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowRunVariable loaded = await _dbContext.WorkflowRunVariables
            .FirstAsync( r => r.VariableName == "src_manual", ct );
        Assert.AreEqual( VariableSource.ManualInput, loaded.Source );
    }

    /// <summary>
    /// Verifies that deleting a workflow run cascades to its run variables.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_CascadeDeleteWithWorkflowRun( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "del_test",
            Value = "\"x\"",
            Version = 1,
            Source = VariableSource.StepOutput,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowRun run = await _dbContext.Set<WorkflowRun>( )
            .FirstAsync( r => r.Id == runId, ct );
        _ = _dbContext.Set<WorkflowRun>( ).Remove( run );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.WorkflowRunVariables.CountAsync( ct );
        Assert.AreEqual( 0, count );
    }

    /// <summary>
    /// Verifies that the VariableName is denormalized — it can reference a name
    /// that does not exist as a WorkflowVariable definition.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_DenormalizedName_NoForeignKey( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        // Insert a run variable with a name that doesn't match any WorkflowVariable
        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "nonexistent_var",
            Value = "\"phantom\"",
            Version = 1,
            Source = VariableSource.StepOutput,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowRunVariable loaded = await _dbContext.WorkflowRunVariables
            .FirstAsync( r => r.VariableName == "nonexistent_var", ct );
        Assert.AreEqual( "\"phantom\"", loaded.Value );
    }

    /// <summary>
    /// Verifies that multiple variables on the same run track independently.
    /// </summary>
    [TestMethod]
    [Timeout( 10_000, CooperativeCancellation = true )]
    public async Task WorkflowRunVariable_MultipleVariables_IndependentVersioning( ) {
        (long _, Guid runId) = await SeedWorkflowRunAsync( TestContext.CancellationToken );
        CancellationToken ct = TestContext.CancellationToken;

        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "alpha",
            Value = "\"a1\"",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        } );
        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "alpha",
            Value = "\"a2\"",
            Version = 2,
            Source = VariableSource.StepOutput,
            Created = DateTime.UtcNow,
        } );
        _ = _dbContext.WorkflowRunVariables.Add( new WorkflowRunVariable {
            WorkflowRunId = runId,
            VariableName = "beta",
            Value = "\"b1\"",
            Version = 1,
            Source = VariableSource.Default,
            Created = DateTime.UtcNow,
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        int alphaCount = await _dbContext.WorkflowRunVariables
            .CountAsync( r => r.VariableName == "alpha", ct );
        int betaCount = await _dbContext.WorkflowRunVariables
            .CountAsync( r => r.VariableName == "beta", ct );

        Assert.AreEqual( 2, alphaCount );
        Assert.AreEqual( 1, betaCount );
    }

    // ── Seed Helpers ────────────────────────────────────────────────────────────

    /// <summary>Seeds a task (required for workflow steps) and returns the task ID.</summary>
    private async Task<long> SeedTaskAsync( CancellationToken ct ) {
        if (!await _dbContext.Tasks.AnyAsync( ct )) {
            _ = _dbContext.Tasks.Add( new WerkrTask {
                Name = "SeedTask",
                Description = "Test task",
                ActionType = TaskActionType.ShellCommand,
                Content = "echo hello",
                TargetTags = ["test"],
            } );
            _ = await _dbContext.SaveChangesAsync( ct );
        }
        return await _dbContext.Tasks.Select( t => t.Id ).FirstAsync( ct );
    }

    /// <summary>Seeds a minimal workflow and returns its ID.</summary>
    private async Task<long> SeedWorkflowAsync( CancellationToken ct ) {
        _ = await SeedTaskAsync( ct );

        Workflow workflow = new( ) {
            Name = $"TestWorkflow_{Guid.NewGuid( ):N}",
            Description = "Test workflow for variable tests",
        };
        _ = _dbContext.Set<Workflow>( ).Add( workflow );
        _ = await _dbContext.SaveChangesAsync( ct );
        return workflow.Id;
    }

    /// <summary>Seeds a workflow and a run, returning both IDs.</summary>
    private async Task<(long WorkflowId, Guid RunId)> SeedWorkflowRunAsync( CancellationToken ct ) {
        long workflowId = await SeedWorkflowAsync( ct );

        Guid runId = Guid.NewGuid( );
        WorkflowRun run = new( ) {
            Id = runId,
            WorkflowId = workflowId,
            StartTime = DateTime.UtcNow,
            Status = WorkflowRunStatus.Running,
        };
        _ = _dbContext.Set<WorkflowRun>( ).Add( run );
        _ = await _dbContext.SaveChangesAsync( ct );
        return (workflowId, runId);
    }
}
