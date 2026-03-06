using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

/// <summary>
/// Unit tests for the <see cref="WorkflowService"/> class, validating CRUD operations on workflows, step management,
/// dependency management, and DAG validation including cycle detection.
/// </summary>
[TestClass]
public class WorkflowServiceTests {
    /// <summary>
    /// The in-memory SQLite connection used for database operations.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The SQLite-backed <see cref="WerkrDbContext"/> used for test data persistence.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="WorkflowService"/> instance under test.
    /// </summary>
    private WorkflowService _service = null!;

    /// <summary>
    /// Gets or sets the MSTest test context for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes an in-memory SQLite database, creates the schema, and instantiates the <see cref="WorkflowService"/>
    /// under test.
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

        _service = new WorkflowService(
            _dbContext,
            NullLogger<WorkflowService>.Instance
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

    // ── CRUD ──

    /// <summary>
    /// Verifies that <see cref="CreateAsync"/> persists a workflow and assigns a positive identifier.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_CreatesWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Test Workflow", Description = "Test" };

        Workflow created = await _service.CreateAsync(
            workflow,
            ct
        );

        Assert.IsGreaterThan(
            0L,
            created.Id
        );
        Assert.AreEqual(
            "Test Workflow",
            created.Name
        );
    }

    /// <summary>
    /// Verifies that creating a workflow with a duplicate name throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_DuplicateName_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow w1 = new( ) { Name = "Dupe", Description = "First" };
        _ = await _service.CreateAsync(
            w1,
            ct
        );

        Workflow w2 = new( ) { Name = "Dupe", Description = "Second" };
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) => _service.CreateAsync(
            w2,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="GetByIdAsync"/> loads a workflow with all steps and dependencies.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_WithStepsAndDependencies_LoadsAll( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Full Load", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        WorkflowStep step1 = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 },
            ct
        );
        WorkflowStep step2 = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 },
            ct
        );
        await _service.AddStepDependencyAsync(
            step2.Id,
            step1.Id,
            ct
        );

        Workflow? loaded = await _service.GetByIdAsync(
            workflow.Id,
            ct
        );

        Assert.IsNotNull( loaded );
        Assert.HasCount(
            2,
            loaded.Steps
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetByIdAsync"/> returns <see langword="null"/> when the workflow does not exist.
    /// </summary>
    [TestMethod]
    public async Task GetByIdAsync_NotFound_ReturnsNull( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow? result = await _service.GetByIdAsync(
            999,
            ct
        );
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="UpdateAsync"/> persists changes to name, description, and enabled status.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_UpdatesFields( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Original", Description = "Desc" };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );

        Workflow update = new( ) { Id = workflow.Id, Name = "Updated", Description = "New", Enabled = false };
        Workflow updated = await _service.UpdateAsync(
            update,
            ct
        );

        Assert.AreEqual(
            "Updated",
            updated.Name
        );
        Assert.IsFalse( updated.Enabled );
    }

    /// <summary>
    /// Verifies that updating a non-existent workflow throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task UpdateAsync_NotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow update = new( ) { Id = 999, Name = "Nope" };

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.UpdateAsync(
            update,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="DeleteAsync"/> removes the workflow so it is no longer retrievable.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_RemovesWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "ToDelete", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );

        await _service.DeleteAsync(
            workflow.Id,
            ct
        );

        Workflow? gone = await _service.GetByIdAsync(
            workflow.Id,
            ct
        );
        Assert.IsNull( gone );
    }

    /// <summary>
    /// Verifies that deleting a non-existent workflow throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task DeleteAsync_NotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.DeleteAsync(
            999,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="GetAllAsync"/> returns all persisted workflows.
    /// </summary>
    [TestMethod]
    public async Task GetAllAsync_ReturnsAllWorkflows( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow w1 = new( ) { Name = "Alpha", Description = string.Empty };
        Workflow w2 = new( ) { Name = "Beta", Description = string.Empty };
        _ = await _service.CreateAsync(
            w1,
            ct
        );
        _ = await _service.CreateAsync(
            w2,
            ct
        );

        IReadOnlyList<Workflow> all = await _service.GetAllAsync( ct );

        Assert.HasCount(
            2,
            all
        );
    }

    // ── Step Management ──

    /// <summary>
    /// Verifies that <see cref="AddStepAsync"/> adds a step and assigns it a positive identifier tied to the workflow.
    /// </summary>
    [TestMethod]
    public async Task AddStepAsync_AddsStepToWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "StepTest", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        WorkflowStep step = new( ) { TaskId = 1, Order = 0 };
        WorkflowStep created = await _service.AddStepAsync(
            workflow.Id,
            step,
            ct
        );

        Assert.IsGreaterThan(
            0L,
            created.Id
        );
        Assert.AreEqual(
            workflow.Id,
            created.WorkflowId
        );
    }

    /// <summary>
    /// Verifies that adding a step to a non-existent workflow throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task AddStepAsync_WorkflowNotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowStep step = new( ) { TaskId = 1, Order = 0 };

        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.AddStepAsync(
            999,
            step,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="RemoveStepAsync"/> deletes the step from the workflow.
    /// </summary>
    [TestMethod]
    public async Task RemoveStepAsync_RemovesStep( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "RemoveStep", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        WorkflowStep step = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 },
            ct
        );

        await _service.RemoveStepAsync(
            step.Id,
            ct
        );

        Workflow? loaded = await _service.GetByIdAsync(
            workflow.Id,
            ct
        );
        Assert.IsEmpty( loaded!.Steps );
    }

    /// <summary>
    /// Verifies that <see cref="UpdateStepAsync"/> persists updated order, control statement, condition, and iteration
    /// fields.
    /// </summary>
    [TestMethod]
    public async Task UpdateStepAsync_UpdatesFields( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "UpdateStep", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        WorkflowStep step = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 },
            ct
        );

        WorkflowStep update = new( ) {
            Id = step.Id,
            TaskId = 1,
            Order = 5,
            ControlStatement = ControlStatement.If,
            ConditionExpression = "$? -eq $true",
            MaxIterations = 50,
        };
        WorkflowStep updated = await _service.UpdateStepAsync(
            update,
            ct
        );

        Assert.AreEqual(
            5,
            updated.Order
        );
        Assert.AreEqual(
            ControlStatement.If,
            updated.ControlStatement
        );
        Assert.AreEqual(
            "$? -eq $true",
            updated.ConditionExpression
        );
        Assert.AreEqual(
            50,
            updated.MaxIterations
        );
    }

    // ── Dependency Management ──

    /// <summary>
    /// Verifies that <see cref="AddStepDependencyAsync"/> persists a dependency between two steps.
    /// </summary>
    [TestMethod]
    public async Task AddStepDependencyAsync_CreatesDependency( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );

        await _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        WorkflowStepDependency? dep = await _dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync(
                d => d.StepId == s2.Id && d.DependsOnStepId == s1.Id,
                ct
            );
        Assert.IsNotNull( dep );
    }

    /// <summary>
    /// Verifies that adding a self-referencing dependency throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestMethod]
    public async Task AddStepDependencyAsync_SelfReference_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) => _service.AddStepDependencyAsync(
            1,
            1,
            ct
        ));
    }

    /// <summary>
    /// Verifies that adding a duplicate step dependency throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestMethod]
    public async Task AddStepDependencyAsync_Duplicate_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) => _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="RemoveStepDependencyAsync"/> deletes the dependency record.
    /// </summary>
    [TestMethod]
    public async Task RemoveStepDependencyAsync_RemovesDependency( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        await _service.RemoveStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        WorkflowStepDependency? dep = await _dbContext.WorkflowStepDependencies
            .FirstOrDefaultAsync(
                d => d.StepId == s2.Id && d.DependsOnStepId == s1.Id,
                ct
            );
        Assert.IsNull( dep );
    }

    // ── DAG Validation ──

    /// <summary>
    /// Verifies that <see cref="ValidateDagAsync"/> returns steps in topological order for a linear DAG.
    /// </summary>
    [TestMethod]
    public async Task ValidateDagAsync_LinearDag_ReturnsTopologicalOrder( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        IReadOnlyList<WorkflowStep> sorted = await _service.ValidateDagAsync(
            s1.WorkflowId,
            ct
        );

        Assert.HasCount(
            2,
            sorted
        );
        Assert.AreEqual(
            s1.Id,
            sorted[0].Id
        );
        Assert.AreEqual(
            s2.Id,
            sorted[1].Id
        );
    }

    /// <summary>
    /// Verifies that <see cref="ValidateDagAsync"/> throws <see cref="InvalidOperationException"/> when a cycle is
    /// detected.
    /// </summary>
    [TestMethod]
    public async Task ValidateDagAsync_CycleDetected_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (WorkflowStep s1, WorkflowStep s2) = await SeedTwoStepsAsync( ct );
        await _service.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );
        await _service.AddStepDependencyAsync(
            s1.Id,
            s2.Id,
            ct
        );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) => _service.ValidateDagAsync(
            s1.WorkflowId,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="ValidateDagAsync"/> returns an empty list for a workflow with no steps.
    /// </summary>
    [TestMethod]
    public async Task ValidateDagAsync_EmptyWorkflow_ReturnsEmpty( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "EmptyDag", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );

        IReadOnlyList<WorkflowStep> sorted = await _service.ValidateDagAsync(
            workflow.Id,
            ct
        );

        Assert.IsEmpty( sorted );
    }

    /// <summary>
    /// Verifies that validating a non-existent workflow throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    [TestMethod]
    public async Task ValidateDagAsync_WorkflowNotFound_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await Assert.ThrowsExactlyAsync<KeyNotFoundException>( ( ) => _service.ValidateDagAsync(
            999,
            ct
        ));
    }

    /// <summary>
    /// Verifies that <see cref="GetTopologicalLevelsAsync"/> groups parallel steps into the same level.
    /// </summary>
    [TestMethod]
    public async Task GetTopologicalLevelsAsync_GroupsParallelSteps( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "Levels", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        // A → B, A → C (B and C are parallel at level 1)
        WorkflowStep a = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 },
            ct
        );
        WorkflowStep b = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 },
            ct
        );
        WorkflowStep c = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 2 },
            ct
        );
        await _service.AddStepDependencyAsync(
            b.Id,
            a.Id,
            ct
        );
        await _service.AddStepDependencyAsync(
            c.Id,
            a.Id,
            ct
        );

        IReadOnlyList<IReadOnlyList<WorkflowStep>> levels =
            await _service.GetTopologicalLevelsAsync(
                workflow.Id,
                ct
            );

        Assert.HasCount(
            2,
            levels
        );
        Assert.HasCount(
            1,
            levels[0]
        ); // Level 0: A
        Assert.HasCount(
            2,
            levels[1]
        ); // Level 1: B, C
    }

    // ── Control Flow Validation ──

    /// <summary>
    /// Verifies that <see cref="ValidateDagAsync"/> throws <see cref="InvalidOperationException"/> for an If step
    /// missing a condition expression.
    /// </summary>
    [TestMethod]
    public async Task ValidateDagAsync_IfWithoutCondition_Throws( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = new( ) { Name = "BadIf", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        _ = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0, ControlStatement = ControlStatement.If, ConditionExpression = null, },
            ct
        );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) => _service.ValidateDagAsync(
            workflow.Id,
            ct
        ));
    }

    // ── Helpers ──

    /// <summary>
    /// Seeds a <see cref="WerkrTask"/> into the database if one does not already exist.
    /// </summary>
    private async Task SeedTaskAsync( CancellationToken ct ) {
        if (!await _dbContext.Tasks.AnyAsync( ct )) {
            _ = _dbContext.Tasks.Add( new WerkrTask { Name = "SeedTask", Description = "Test task", ActionType = TaskActionType.ShellCommand, Content = "echo hello", TargetTags = ["test"], } );
            _ = await _dbContext.SaveChangesAsync( ct );
        }
    }

    /// <summary>
    /// Seeds a workflow with two steps and returns both steps.
    /// </summary>
    private async Task<(WorkflowStep S1, WorkflowStep S2)> SeedTwoStepsAsync( CancellationToken ct ) {
        Workflow workflow = new( ) { Name = $"W_{Guid.NewGuid( ):N}", Description = string.Empty };
        _ = await _service.CreateAsync(
            workflow,
            ct
        );
        await SeedTaskAsync( ct );

        WorkflowStep s1 = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 0 },
            ct
        );
        WorkflowStep s2 = await _service.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = 1, Order = 1 },
            ct
        );

        return(
            s1,
            s2
        );
    }
}
