using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

[TestClass]
public class WorkflowServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private WorkflowService _service = null!;

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

        _service = new WorkflowService( _dbContext, NullLogger<WorkflowService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // ── CRUD ──

    [TestMethod]
    public async Task CreateAsync_CreatesWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Test Workflow", Description = "Test" };

        Workflow created = await _service.CreateAsync( workflow, ct );

        Assert.IsGreaterThan( 0L, created.Id );
        Assert.AreEqual( "Test Workflow", created.Name );
    }

    [TestMethod]
    public async Task CreateAsync_DuplicateName_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow w1 = new( ) { Name = "Dupe", Description = "First" };
        _ = await _service.CreateAsync( w1, ct );

        Workflow w2 = new( ) { Name = "Dupe", Description = "Second" };
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.CreateAsync( w2, ct ) );
    }

    [TestMethod]
    public async Task GetByIdAsync_WithStepsAndDependencies_LoadsAll( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Full Load", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        WorkflowStep step1 = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 }, ct );
        WorkflowStep step2 = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 }, ct );
        await _service.AddStepDependencyAsync( step2.Id, step1.Id, ct );

        Workflow? loaded = await _service.GetByIdAsync( workflow.Id, ct );

        Assert.IsNotNull( loaded );
        Assert.HasCount( 2, loaded.Steps );
    }

    [TestMethod]
    public async Task GetByIdAsync_NotFound_ReturnsNull( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow? result = await _service.GetByIdAsync( 999, ct );
        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task UpdateAsync_UpdatesFields( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Original", Description = "Desc" };
        _ = await _service.CreateAsync( workflow, ct );

        Workflow update = new( ) { Id = workflow.Id, Name = "Updated", Description = "New", Enabled = false };
        Workflow updated = await _service.UpdateAsync( update, ct );

        Assert.AreEqual( "Updated", updated.Name );
        Assert.IsFalse( updated.Enabled );
    }

    [TestMethod]
    public async Task UpdateAsync_NotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow update = new( ) { Id = 999, Name = "Nope" };

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.UpdateAsync( update, ct ) );
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "ToDelete", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );

        await _service.DeleteAsync( workflow.Id, ct );

        Workflow? gone = await _service.GetByIdAsync( workflow.Id, ct );
        Assert.IsNull( gone );
    }

    [TestMethod]
    public async Task DeleteAsync_NotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.DeleteAsync( 999, ct ) );
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsAllWorkflows( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow w1 = new( ) { Name = "Alpha", Description = "" };
        Workflow w2 = new( ) { Name = "Beta", Description = "" };
        _ = await _service.CreateAsync( w1, ct );
        _ = await _service.CreateAsync( w2, ct );

        IReadOnlyList<Workflow> all = await _service.GetAllAsync( ct );

        Assert.HasCount( 2, all );
    }

    // ── Step Management ──

    [TestMethod]
    public async Task AddStepAsync_AddsStepToWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "StepTest", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        WorkflowStep step = new( ) { TaskId = 1, Order = 0 };
        WorkflowStep created = await _service.AddStepAsync( workflow.Id, step, ct );

        Assert.IsGreaterThan( 0L, created.Id );
        Assert.AreEqual( workflow.Id, created.WorkflowId );
    }

    [TestMethod]
    public async Task AddStepAsync_WorkflowNotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowStep step = new( ) { TaskId = 1, Order = 0 };

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.AddStepAsync( 999, step, ct ) );
    }

    [TestMethod]
    public async Task RemoveStepAsync_RemovesStep( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "RemoveStep", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        WorkflowStep step = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 }, ct );

        await _service.RemoveStepAsync( step.Id, ct );

        Workflow? loaded = await _service.GetByIdAsync( workflow.Id, ct );
        Assert.IsEmpty( loaded!.Steps );
    }

    [TestMethod]
    public async Task UpdateStepAsync_UpdatesFields( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "UpdateStep", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        WorkflowStep step = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 }, ct );

        WorkflowStep update = new( ) {
            Id = step.Id,
            TaskId = 1,
            Order = 5,
            ControlStatement = ControlStatement.If,
            ConditionExpression = "$? -eq $true",
            MaxIterations = 50,
        };
        WorkflowStep updated = await _service.UpdateStepAsync( update, ct );

        Assert.AreEqual( 5, updated.Order );
        Assert.AreEqual( ControlStatement.If, updated.ControlStatement );
        Assert.AreEqual( "$? -eq $true", updated.ConditionExpression );
        Assert.AreEqual( 50, updated.MaxIterations );
    }

    // ── Dependency Management ──

    [TestMethod]
    public async Task AddStepDependencyAsync_CreatesDependency( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );

        await _service.AddStepDependencyAsync( s2.Id, s1.Id, ct );

        WorkflowStepDependency? dep = await _dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync( d => d.StepId == s2.Id && d.DependsOnStepId == s1.Id, ct );
        Assert.IsNotNull( dep );
    }

    [TestMethod]
    public async Task AddStepDependencyAsync_SelfReference_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.AddStepDependencyAsync( 1, 1, ct ) );
    }

    [TestMethod]
    public async Task AddStepDependencyAsync_Duplicate_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync( s2.Id, s1.Id, ct );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.AddStepDependencyAsync( s2.Id, s1.Id, ct ) );
    }

    [TestMethod]
    public async Task RemoveStepDependencyAsync_RemovesDependency( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync( s2.Id, s1.Id, ct );

        await _service.RemoveStepDependencyAsync( s2.Id, s1.Id, ct );

        WorkflowStepDependency? dep = await _dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync( d => d.StepId == s2.Id && d.DependsOnStepId == s1.Id, ct );
        Assert.IsNull( dep );
    }

    // ── DAG Validation ──

    [TestMethod]
    public async Task ValidateDagAsync_LinearDag_ReturnsTopologicalOrder( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync( s2.Id, s1.Id, ct );

        IReadOnlyList<WorkflowStep> sorted = await _service.ValidateDagAsync(
            s1.WorkflowId, ct );

        Assert.HasCount( 2, sorted );
        Assert.AreEqual( s1.Id, sorted[0].Id );
        Assert.AreEqual( s2.Id, sorted[1].Id );
    }

    [TestMethod]
    public async Task ValidateDagAsync_CycleDetected_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync( s2.Id, s1.Id, ct );
        await _service.AddStepDependencyAsync( s1.Id, s2.Id, ct );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.ValidateDagAsync( s1.WorkflowId, ct ) );
    }

    [TestMethod]
    public async Task ValidateDagAsync_EmptyWorkflow_ReturnsEmpty( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "EmptyDag", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );

        IReadOnlyList<WorkflowStep> sorted = await _service.ValidateDagAsync( workflow.Id, ct );

        Assert.IsEmpty( sorted );
    }

    [TestMethod]
    public async Task ValidateDagAsync_WorkflowNotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>(
            ( ) => _service.ValidateDagAsync( 999, ct ) );
    }

    [TestMethod]
    public async Task GetTopologicalLevelsAsync_GroupsParallelSteps( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Levels", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        // A → B, A → C (B and C are parallel at level 1)
        WorkflowStep a = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 }, ct );
        WorkflowStep b = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 }, ct );
        WorkflowStep c = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 2 }, ct );
        await _service.AddStepDependencyAsync( b.Id, a.Id, ct );
        await _service.AddStepDependencyAsync( c.Id, a.Id, ct );

        IReadOnlyList<IReadOnlyList<WorkflowStep>> levels =
            await _service.GetTopologicalLevelsAsync( workflow.Id, ct );

        Assert.HasCount( 2, levels );
        Assert.HasCount( 1, levels[0] ); // Level 0: A
        Assert.HasCount( 2, levels[1] ); // Level 1: B, C
    }

    // ── Control Flow Validation ──

    [TestMethod]
    public async Task ValidateDagAsync_IfWithoutCondition_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "BadIf", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        _ = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep {
                TaskId = 1,
                Order = 0,
                ControlStatement = ControlStatement.If,
                ConditionExpression = null,
            }, ct );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.ValidateDagAsync( workflow.Id, ct ) );
    }

    // ── Helpers ──

    private async Task SeedTaskAsync( CancellationToken ct ) {
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
    }

    private async Task<(WorkflowStep S1, WorkflowStep S2)> SeedTwoStepsAsync( CancellationToken ct ) {
        Workflow workflow = new( ) { Name = $"W_{Guid.NewGuid( ):N}", Description = "" };
        _ = await _service.CreateAsync( workflow, ct );
        await SeedTaskAsync( ct );

        WorkflowStep s1 = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 }, ct );
        WorkflowStep s2 = await _service.AddStepAsync( workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 }, ct );

        return (s1, s2);
    }
}
