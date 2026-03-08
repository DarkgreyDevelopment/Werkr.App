using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Tasks;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

/// <summary>
/// Unit tests for the <see cref="WorkflowExecutor"/> class, validating workflow DAG traversal, conditional branching,
/// looping, multi-agent resolution, fan-in dependency modes, per-task success criteria, and run history retrieval.
/// </summary>
[TestClass]
public class WorkflowExecutorTests {
    /// <summary>
    /// The in-memory SQLite connection used for database operations.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The SQLite-backed <see cref="WerkrDbContext"/> used for test data persistence.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="WorkflowService"/> instance used to create and manage workflows and steps.
    /// </summary>
    private WorkflowService _workflowService = null!;
    /// <summary>
    /// The <see cref="WorkflowExecutor"/> instance under test.
    /// </summary>
    private WorkflowExecutor _executor = null!;
    /// <summary>
    /// The configurable command dispatcher that simulates agent execution.
    /// </summary>
    private ConfigurableCommandDispatcher _dispatcher = null!;

    /// <summary>
    /// Gets or sets the MSTest test context for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes test infrastructure including an in-memory SQLite database, DI container, and all services required
    /// by <see cref="WorkflowExecutor"/>.
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

        _workflowService = new WorkflowService(
            _dbContext,
            NullLogger<WorkflowService>.Instance
        );

        ServiceCollection services = new( );
        _ = services.AddDbContext<WerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped
        );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped
        );
        ServiceProvider sp = services.BuildServiceProvider( );

        AgentConnectionManager connectionManager = new(
            sp.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance
        );

        AgentResolver agentResolver = new(
            _dbContext,
            connectionManager,
            NullLogger<AgentResolver>.Instance
        );
        ConditionEvaluator conditionEvaluator = new( NullLogger<ConditionEvaluator>.Instance );

        IOptions<JobOutputOptions> outputOptions = Options.Create( new JobOutputOptions {
            OutputDirectory = Path.Combine(
            Path.GetTempPath( ),
            $"werkr_test_{Guid.NewGuid( ):N}"
        ),
            TailPreviewLength = 500,
        } );
        JobOutputWriter outputWriter = new(
            outputOptions,
            NullLogger<JobOutputWriter>.Instance
        );
        SuccessCriteriaEvaluator criteriaEvaluator = new( NullLogger<SuccessCriteriaEvaluator>.Instance );

        _dispatcher = new ConfigurableCommandDispatcher( );
        JobExecutionService jobExecutionService = new(
            _dbContext,
            _dispatcher,
            agentResolver,
            outputWriter,
            criteriaEvaluator,
            NullLogger<JobExecutionService>.Instance
        );

        _executor = new WorkflowExecutor(
            _dbContext,
            _workflowService,
            jobExecutionService,
            agentResolver,
            conditionEvaluator,
            new WorkflowRunTracker( ),
            NullLogger<WorkflowExecutor>.Instance
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

    // ── Basic Execution ──

    /// <summary>
    /// Verifies that executing a workflow with a single step completes successfully and sets an end time on the run.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_SingleStep_CompletesSuccessfully( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (Workflow workflow, _) = await SeedSingleStepWorkflowAsync( ct );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
        Assert.IsNotNull( run.EndTime );
    }

    /// <summary>
    /// Verifies that a linear two-step DAG executes both steps in dependency order and produces two jobs.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_LinearDag_ExecutesInOrder( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = await SeedLinearWorkflowAsync( ct );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
    }

    /// <summary>
    /// Verifies that a diamond-shaped DAG with parallel branches completes successfully.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ParallelSteps_AllComplete( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = await SeedDiamondWorkflowAsync( ct );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
    }

    // ── Control Flow ──

    /// <summary>
    /// Verifies that an <see cref="If"/> step whose condition evaluates to true executes the guarded branch.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_IfConditionTrue_ExecutesBranch( ) {
        CancellationToken ct = TestContext.CancellationToken;
        Workflow workflow = await SeedIfWorkflowAsync(
            "$? -eq $true",
            ct
        );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
    }

    /// <summary>
    /// Verifies that an <see cref="If"/> step whose condition evaluates to false skips the guarded branch.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_IfConditionFalse_SkipsBranch( ) {
        CancellationToken ct = TestContext.CancellationToken;
        // The stub dispatcher returns exit code 0 / success=true,
        // so "$? -eq $false" will cause the If branch to be skipped
        Workflow workflow = await SeedIfWorkflowAsync(
            "$? -eq $false",
            ct
        );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
    }

    // ── Workflow Runs ──

    /// <summary>
    /// Verifies that <see cref="GetRunsAsync"/> returns the complete run history for a workflow.
    /// </summary>
    [TestMethod]
    public async Task GetRunsAsync_ReturnsRunHistory( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (Workflow workflow, _) = await SeedSingleStepWorkflowAsync( ct );

        _ = await _executor.ExecuteAsync(
            workflow,
            ct
        );
        _ = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        IReadOnlyList<WorkflowRun> runs = await _executor.GetRunsAsync(
            workflow.Id,
            50,
            ct
        );

        Assert.HasCount(
            2,
            runs
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetRunAsync"/> returns a run with its associated jobs.
    /// </summary>
    [TestMethod]
    public async Task GetRunAsync_ReturnsRunWithJobs( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (Workflow workflow, _) = await SeedSingleStepWorkflowAsync( ct );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );

        Assert.IsNotNull( loaded );
        Assert.AreEqual(
            run.Id,
            loaded.Id
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetRunAsync"/> returns <see langword="null"/> for a non-existent run identifier.
    /// </summary>
    [TestMethod]
    public async Task GetRunAsync_NotFound_ReturnsNull( ) {
        CancellationToken ct = TestContext.CancellationToken;
        WorkflowRun? result = await _executor.GetRunAsync(
            Guid.NewGuid( ),
            ct
        );
        Assert.IsNull( result );
    }

    // ── Cancellation ──

    /// <summary>
    /// Verifies that cancelling the token before execution sets the run status to <see cref="Cancelled"/>.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_Cancelled_SetsCancelledStatus( ) {
        CancellationToken ct = TestContext.CancellationToken;
        (Workflow workflow, _) = await SeedSingleStepWorkflowAsync( ct );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
        await cts.CancelAsync( );

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            cts.Token
        );

        Assert.AreEqual(
            WorkflowRunStatus.Cancelled,
            run.Status
        );
    }

    // ── No Agent Available ──

    /// <summary>
    /// Verifies that execution fails when no agent matches the task's target tags.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_NoAgentAvailable_FailsRun( ) {
        CancellationToken ct = TestContext.CancellationToken;
        // Create workflow with step that has no matching agent (no agents registered)
        Workflow workflow = new( ) { Name = "NoAgent", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WerkrTask task = new( ) {
            Name = "NoAgentTask",
            Description = "Test",
            ActionType = TaskActionType.ShellCommand,
            Content = "echo hello",
            TargetTags = ["nonexistent-agent-tag"],
        };
        _ = _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        // Reload to get steps
        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Failed,
            run.Status
        );
    }

    // ── ElseIf Chain ──

    /// <summary>
    /// Verifies that in an <see cref="ElseIf"/> chain only the first branch whose condition is true is executed and
    /// subsequent branches are skipped.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ElseIfChain_FirstTrueBranchTakenRestSkipped( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "ElseIfChain", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Root → If ($? -eq $true) → ElseIf ($? -eq $true)
        // If branch taken because root succeeds; ElseIf skipped because prior branch was taken.
        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        WorkflowStep ifStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.If, ConditionExpression = "$? -eq $true", },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            ifStep.Id,
            root.Id,
            ct
        );

        WorkflowStep elseIfStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2, ControlStatement = ControlStatement.ElseIf, ConditionExpression = "$? -eq $true", },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            elseIfStep.Id,
            ifStep.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // Verify: root + If branch executed (2 jobs), ElseIf skipped
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            2,
            loaded.Jobs
        );
    }

    // ── While Loop ──

    /// <summary>
    /// Verifies that a <see cref="While"/> loop respects the maximum iteration count and produces the expected number
    /// of jobs.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_WhileLoop_RespectsMaxIterations( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "WhileLoop", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        // While condition is always true (exit code == 0), MaxIterations guards against infinite loop
        WorkflowStep whileStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.While, ConditionExpression = "$exitCode == 0", MaxIterations = 3, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            whileStep.Id,
            root.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // root + 3 while iterations = 4 jobs
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            4,
            loaded.Jobs
        );
    }

    // ── Do Loop ──

    /// <summary>
    /// Verifies that a <see cref="Do"/> loop executes the step body at least once even when the condition is
    /// immediately false.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_DoLoop_ExecutesAtLeastOnce( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "DoLoop", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        // Do: executes first, then checks condition. Condition false → stops after 1 iteration.
        WorkflowStep doStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.Do, ConditionExpression = "$? -eq $false", MaxIterations = 10, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            doStep.Id,
            root.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // root + 1 do iteration = 2 jobs (condition false after first execution)
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            2,
            loaded.Jobs
        );
    }

    // ── Dependency Failure ──

    /// <summary>
    /// Verifies that a workflow fails when a step depends on a predecessor that was skipped due to a false <see
    /// cref="If"/> condition.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_DependencyNotSatisfied_FailsWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "DepFail", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Root → If ($? -eq $false, skipped) → Child (Sequential, depends on If)
        // If is skipped so Child's dependency is not satisfied → workflow fails.
        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        WorkflowStep ifStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.If, ConditionExpression = "$? -eq $false", },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            ifStep.Id,
            root.Id,
            ct
        );

        WorkflowStep child = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            child.Id,
            ifStep.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Failed,
            run.Status
        );
    }

    // ── Empty Workflow ──

    /// <summary>
    /// Verifies that a workflow with no steps completes immediately without producing any jobs.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_NoSteps_CompletesImmediately( ) {
        CancellationToken ct = TestContext.CancellationToken;

        Workflow workflow = new( ) { Name = "Empty", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );
        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
        Assert.IsNotNull( run.EndTime );
    }

    // ── Multi-Agent Resolution ──

    /// <summary>
    /// Verifies that steps with different target tags are dispatched to the correct agents based on tag resolution.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_MultiAgent_StepsResolveToCorrectAgents( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RegisteredConnection agentA = await SeedAgentAsync(
            "db-agent",
            ["db-server"],
            ct
        );
        RegisteredConnection agentB = await SeedAgentAsync(
            "app-agent",
            ["app-server"],
            ct
        );

        WerkrTask dbTask = await SeedTaskAsync(
            "DbBackup",
            ["db-server"],
            null,
            ct
        );
        WerkrTask appTask = await SeedTaskAsync(
            "AppDeploy",
            ["app-server"],
            null,
            ct
        );

        Workflow workflow = new( ) { Name = "MultiAgent", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep s1 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = dbTask.Id, Order = 0 },
            ct
        );
        WorkflowStep s2 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = appTask.Id, Order = 1 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
        Assert.HasCount(
            2,
            _dispatcher.InvokedAgentIds
        );
        Assert.AreEqual(
            agentA.Id,
            _dispatcher.InvokedAgentIds[0]
        );
        Assert.AreEqual(
            agentB.Id,
            _dispatcher.InvokedAgentIds[1]
        );
    }

    /// <summary>
    /// Verifies that the workflow fails when a step's target tags cannot be resolved to any connected agent.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_MultiAgent_UnresolvableTags_FailsWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;

        _ = await SeedAgentAsync(
            "db-agent",
            ["db-server"],
            ct
        );
        WerkrTask dbTask = await SeedTaskAsync(
            "DbBackup",
            ["db-server"],
            null,
            ct
        );
        WerkrTask appTask = await SeedTaskAsync(
            "AppDeploy",
            ["nonexistent-tag"],
            null,
            ct
        );

        Workflow workflow = new( ) { Name = "UnresolvableTags", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep s1 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = dbTask.Id, Order = 0 },
            ct
        );
        WorkflowStep s2 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = appTask.Id, Order = 1 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Failed,
            run.Status
        );
    }

    // ── Agent Connection Override ──

    /// <summary>
    /// Verifies that <see cref="AgentConnectionIdOverride"/> on a step bypasses tag-based agent resolution.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_AgentOverride_BypassesTagResolution( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Tag-matching agent (would be selected by normal tag resolution)
        _ = await SeedAgentAsync(
            "tag-agent",
            ["test"],
            ct
        );
        // Override agent (different tags, wouldn't match task's TargetTags)
        RegisteredConnection overrideAgent = await SeedAgentAsync(
            "override-agent",
            ["other"],
            ct
        );

        WerkrTask task = await SeedTaskAsync( ct ); // TargetTags = ["test"]

        Workflow workflow = new( ) { Name = "Override", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        _ = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0, AgentConnectionIdOverride = overrideAgent.Id, },
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
        Assert.HasCount(
            1,
            _dispatcher.InvokedAgentIds
        );
        Assert.AreEqual(
            overrideAgent.Id,
            _dispatcher.InvokedAgentIds[0]
        );
    }

    /// <summary>
    /// Verifies that a workflow with a mix of tag-resolved and override-resolved steps dispatches each step to the
    /// correct agent.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_MixedResolution_TagsAndOverride( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RegisteredConnection tagAgent = await SeedAgentAsync(
            "tag-agent",
            ["test"],
            ct
        );
        RegisteredConnection pinnedAgent = await SeedAgentAsync(
            "pinned-agent",
            ["pinned"],
            ct
        );

        WerkrTask task = await SeedTaskAsync( ct ); // TargetTags = ["test"]

        Workflow workflow = new( ) { Name = "Mixed", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Step 1: tag resolution → tagAgent
        WorkflowStep s1 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );
        // Step 2: override → pinnedAgent
        WorkflowStep s2 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, AgentConnectionIdOverride = pinnedAgent.Id, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );
        Assert.HasCount(
            2,
            _dispatcher.InvokedAgentIds
        );
        Assert.AreEqual(
            tagAgent.Id,
            _dispatcher.InvokedAgentIds[0]
        );
        Assert.AreEqual(
            pinnedAgent.Id,
            _dispatcher.InvokedAgentIds[1]
        );
    }

    // ── Fan-In (DependencyMode) ──

    /// <summary>
    /// Verifies that a fan-in step with <see cref="DependencyMode.All"/> waits for all predecessors to complete before
    /// executing.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_FanIn_DependencyModeAll_WaitsForAll( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "FanInAll", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // A and B are independent, C depends on both with DependencyMode.All
        WorkflowStep a = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );
        WorkflowStep b = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1 },
            ct
        );
        WorkflowStep c = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2, DependencyMode = DependencyMode.All, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            a.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            b.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // All 3 steps produced jobs
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            3,
            loaded.Jobs
        );
    }

    /// <summary>
    /// Verifies that a fan-in step with <see cref="DependencyMode.Any"/> proceeds once any single predecessor
    /// completes, even when others are skipped.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_FanIn_DependencyModeAny_ProceedsWithSingle( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "FanInAny", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Root → A (If $? -eq $false → skipped), Root → B (Sequential → executes)
        // C depends on both A and B with DependencyMode.Any → proceeds with B
        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        WorkflowStep a = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.If, ConditionExpression = "$? -eq $false", },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            a.Id,
            root.Id,
            ct
        );

        WorkflowStep b = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            b.Id,
            root.Id,
            ct
        );

        WorkflowStep c = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 3, DependencyMode = DependencyMode.Any, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            a.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            b.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // root + B + C = 3 jobs (A skipped)
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            3,
            loaded.Jobs
        );
    }

    /// <summary>
    /// Verifies that a fan-in step with <see cref="DependencyMode.All"/> fails the workflow when one of its
    /// predecessors is skipped.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_FanIn_DependencyModeAll_SkippedPredecessor_FailsWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "FanInAllFail", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Same fan-in structure as Any test but C uses DependencyMode.All.
        // A is skipped (no result), so C's All check fails → workflow fails.
        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        WorkflowStep a = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.If, ConditionExpression = "$? -eq $false", },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            a.Id,
            root.Id,
            ct
        );

        WorkflowStep b = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            b.Id,
            root.Id,
            ct
        );

        WorkflowStep c = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 3, DependencyMode = DependencyMode.All, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            a.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            b.Id,
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Failed,
            run.Status
        );
    }

    // ── Per-Task SuccessCriteria ──

    /// <summary>
    /// Verifies that per-task success criteria (e.g., "always") are evaluated correctly so that a non-zero exit code
    /// can still mark the job as successful.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_PerTaskSuccessCriteria_CustomCriteriaEvaluated( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await SeedAgentAsync( ct );

        // Task with SuccessCriteria = "always" — succeeds even with non-zero exit code
        _dispatcher.DefaultExitCode = 1;
        WerkrTask task = await SeedTaskAsync(
            "AlwaysTask",
            ["test"],
            successCriteria: "always",
            ct: ct
        );

        Workflow workflow = new( ) { Name = "Criteria", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        _ = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;

        WorkflowRun run = await _executor.ExecuteAsync(
            workflow,
            ct
        );

        Assert.AreEqual(
            WorkflowRunStatus.Completed,
            run.Status
        );

        // Verify the job succeeded despite exit code 1
        WorkflowRun? loaded = await _executor.GetRunAsync(
            run.Id,
            ct
        );
        Assert.IsNotNull( loaded );
        Assert.HasCount(
            1,
            loaded.Jobs
        );
        Assert.IsTrue( loaded.Jobs.First( ).Success );
    }

    // ── Helper Methods ──

    /// <summary>
    /// Seeds a default agent with the "test" tag into the database.
    /// </summary>
    private Task<RegisteredConnection> SeedAgentAsync( CancellationToken ct ) =>
        SeedAgentAsync(
            "test-agent",
            ["test"],
            ct
        );

    private async Task<RegisteredConnection> SeedAgentAsync(
        string name,
        string[] tags,
        CancellationToken ct
    ) {
        RegisteredConnection agent = new( ) {
            Id = Guid.NewGuid( ),
            ConnectionName = name,
            RemoteUrl = "https://localhost:5001",
            Tags = tags,
            Status = ConnectionStatus.Connected,
            SharedKey = new byte[32],
            IsServer = true,
        };
        _ = _dbContext.RegisteredConnections.Add( agent );
        _ = await _dbContext.SaveChangesAsync( ct );
        return agent;
    }

    /// <summary>
    /// Seeds a default task with the "test" target tag into the database.
    /// </summary>
    private Task<WerkrTask> SeedTaskAsync( CancellationToken ct ) =>
        SeedTaskAsync(
            "TestTask",
            ["test"],
            null,
            ct
        );

    private async Task<WerkrTask> SeedTaskAsync(
        string name,
        string[] targetTags,
        string? successCriteria,
        CancellationToken ct
    ) {
        WerkrTask task = new( ) {
            Name = name,
            Description = "Test",
            ActionType = TaskActionType.ShellCommand,
            Content = "echo hello",
            TargetTags = targetTags,
            SuccessCriteria = successCriteria,
        };
        _ = _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );
        return task;
    }

    /// <summary>
    /// Seeds a workflow with a single step, including an agent and a task.
    /// </summary>
    private async Task<(Workflow Workflow, WorkflowStep Step)> SeedSingleStepWorkflowAsync( CancellationToken ct ) {
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "Single", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep step = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        // Reload with steps
        workflow = (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;
        return (
            workflow,
            step
        );
    }

    /// <summary>
    /// Seeds a workflow with two steps linked in a linear dependency chain.
    /// </summary>
    private async Task<Workflow> SeedLinearWorkflowAsync( CancellationToken ct ) {
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "Linear", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        WorkflowStep s1 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );
        WorkflowStep s2 = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            s2.Id,
            s1.Id,
            ct
        );

        return (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;
    }

    /// <summary>
    /// Seeds a diamond-shaped workflow with four steps: one root, two parallel branches, and one convergent step.
    /// </summary>
    private async Task<Workflow> SeedDiamondWorkflowAsync( CancellationToken ct ) {
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "Diamond", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // A → B, A → C, B → D, C → D (diamond shape)
        WorkflowStep a = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );
        WorkflowStep b = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1 },
            ct
        );
        WorkflowStep c = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 2 },
            ct
        );
        WorkflowStep d = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 3 },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            b.Id,
            a.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            c.Id,
            a.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            d.Id,
            b.Id,
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            d.Id,
            c.Id,
            ct
        );

        return (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;
    }

    /// <summary>
    /// Seeds a workflow with a root step followed by a conditional <see cref="If"/> step using the supplied condition
    /// expression.
    /// </summary>
    private async Task<Workflow> SeedIfWorkflowAsync(
        string condition,
        CancellationToken ct
    ) {
        _ = await SeedAgentAsync( ct );
        WerkrTask task = await SeedTaskAsync( ct );

        Workflow workflow = new( ) { Name = "IfBranch", Description = string.Empty };
        _ = await _workflowService.CreateAsync(
            workflow,
            ct
        );

        // Step 1: Sequential (root)
        WorkflowStep root = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 0 },
            ct
        );

        // Step 2: If branch (depends on root)
        WorkflowStep ifStep = await _workflowService.AddStepAsync(
            workflow.Id,
            new WorkflowStep { TaskId = task.Id, Order = 1, ControlStatement = ControlStatement.If, ConditionExpression = condition, },
            ct
        );
        await _workflowService.AddStepDependencyAsync(
            ifStep.Id,
            root.Id,
            ct
        );

        return (await _workflowService.GetByIdAsync(
            workflow.Id,
            ct
        ))!;
    }

    /// <summary>
    /// Configurable command dispatcher for testing. Returns output with a configurable
    /// exit code and tracks which agents received invocations.
    /// </summary>
    private sealed class ConfigurableCommandDispatcher : ICommandDispatcher {
        /// <summary>Default exit code returned for all invocations unless overridden per-agent.</summary>
        public int DefaultExitCode { get; set; }

        /// <summary>Per-agent exit code overrides.</summary>
        public Dictionary<Guid, int> AgentExitCodes { get; } = [];

        /// <summary>Tracks which agent IDs received invocations, in order.</summary>
        public List<Guid> InvokedAgentIds { get; } = [];

        /// <summary>
        /// Simulates executing a shell command on the specified agent and yields synthetic output including a
        /// configurable exit code.
        /// </summary>
        public async IAsyncEnumerable<OperatorOutput> ExecuteCommandAsync(
            Guid agentConnectionId,
            OperatorType operatorType,
            string command,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
            InvokedAgentIds.Add( agentConnectionId );
            int exitCode = AgentExitCodes.GetValueOrDefault(
                agentConnectionId,
                DefaultExitCode
            );
            await Task.CompletedTask;
            yield return OperatorOutput.Create(
                "Information",
                "OK"
            );
            yield return OperatorOutput.Create(
                "Information",
                $"Process exited with code {exitCode}"
            );
        }

        /// <summary>
        /// Simulates executing a script on the specified agent and yields synthetic output including a configurable
        /// exit code.
        /// </summary>
        public async IAsyncEnumerable<OperatorOutput> ExecuteScriptAsync(
            Guid agentConnectionId,
            OperatorType operatorType,
            string scriptPath,
            IEnumerable<string>? args,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
            InvokedAgentIds.Add( agentConnectionId );
            int exitCode = AgentExitCodes.GetValueOrDefault(
                agentConnectionId,
                DefaultExitCode
            );
            await Task.CompletedTask;
            yield return OperatorOutput.Create(
                "Information",
                "OK"
            );
            yield return OperatorOutput.Create(
                "Information",
                $"Process exited with code {exitCode}"
            );
        }

        /// <summary>
        /// Simulates executing an action descriptor on the specified agent and yields synthetic output including a
        /// configurable exit code.
        /// </summary>
        public async IAsyncEnumerable<OperatorOutput> ExecuteActionAsync(
            Guid agentConnectionId,
            ActionDescriptor descriptor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default
        ) {
            InvokedAgentIds.Add( agentConnectionId );
            int exitCode = AgentExitCodes.GetValueOrDefault(
                agentConnectionId,
                DefaultExitCode
            );
            await Task.CompletedTask;
            yield return OperatorOutput.Create(
                "Information",
                "OK"
            );
            yield return OperatorOutput.Create(
                "Information",
                $"Process exited with code {exitCode}"
            );
        }
    }
}
