using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Tests.Data.Unit.Workflows;

/// <summary>
/// Tests that the <c>ControlStatementStringConverter</c> in <see cref="WerkrDbContext"/>
/// correctly round-trips <see cref="ControlStatement"/> enum values through the database
/// as strings, including backward-compatible reading of the legacy <c>"Sequential"</c> value.
/// </summary>
[TestClass]
public class ControlStatementConverterTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .UseSnakeCaseNamingConvention( )
            .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Creates a workflow step with <see cref="ControlStatement.Default"/>, saves, reloads,
    /// and verifies it round-trips correctly and is stored as the string <c>"Default"</c>.
    /// </summary>
    [TestMethod]
    public async Task Default_RoundTrips_AsDefaultString( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Arrange — create a workflow and task to host the step
        Workflow workflow = new( ) { Name = "RoundTrip_WF", Description = "test" };
        _dbContext.Workflows.Add( workflow );
        WerkrTask task = new( ) { Name = "RoundTrip_Task", ActionType = TaskActionType.ShellCommand, Content = "echo test", TargetTags = ["test"] };
        _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowStep step = new( ) {
            WorkflowId = workflow.Id,
            TaskId = task.Id,
            Order = 0,
            ControlStatement = ControlStatement.Default,
        };
        _dbContext.WorkflowSteps.Add( step );
        _ = await _dbContext.SaveChangesAsync( ct );

        // Detach so the next query hits the database
        _dbContext.ChangeTracker.Clear( );

        // Act — reload from DB
        WorkflowStep loaded = await _dbContext.WorkflowSteps.SingleAsync( s => s.Id == step.Id, ct );

        // Assert — enum value round-trips
        Assert.AreEqual( ControlStatement.Default, loaded.ControlStatement );

        // Assert — raw string in the database is "Default"
        long stepId = step.Id;
        string? raw = await _dbContext.Database
            .SqlQuery<string>( $"SELECT control_statement AS Value FROM workflow_steps WHERE id = {stepId}" )
            .SingleAsync( ct );
        Assert.AreEqual( "Default", raw );
    }

    /// <summary>
    /// Verifies that every non-Default enum member round-trips through the database correctly.
    /// </summary>
    [TestMethod]
    [DataRow( ControlStatement.If, "If" )]
    [DataRow( ControlStatement.Else, "Else" )]
    [DataRow( ControlStatement.ElseIf, "ElseIf" )]
    [DataRow( ControlStatement.While, "While" )]
    [DataRow( ControlStatement.Do, "Do" )]
    public async Task AllEnumValues_RoundTrip_Correctly( ControlStatement value, string expectedString ) {
        CancellationToken ct = TestContext.CancellationToken;

        Workflow workflow = new( ) { Name = $"RoundTrip_{value}", Description = "test" };
        _dbContext.Workflows.Add( workflow );
        WerkrTask task = new( ) { Name = $"Task_{value}", ActionType = TaskActionType.ShellCommand, Content = "echo test", TargetTags = ["test"] };
        _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        WorkflowStep step = new( ) {
            WorkflowId = workflow.Id,
            TaskId = task.Id,
            Order = 0,
            ControlStatement = value,
            ConditionExpression = value is ControlStatement.If or ControlStatement.ElseIf or ControlStatement.While or ControlStatement.Do
                ? "$? -eq $true" : null,
        };
        _dbContext.WorkflowSteps.Add( step );
        _ = await _dbContext.SaveChangesAsync( ct );

        _dbContext.ChangeTracker.Clear( );

        WorkflowStep loaded = await _dbContext.WorkflowSteps.SingleAsync( s => s.Id == step.Id, ct );
        Assert.AreEqual( value, loaded.ControlStatement );

        long stepId = step.Id;
        string? raw = await _dbContext.Database
            .SqlQuery<string>( $"SELECT control_statement AS Value FROM workflow_steps WHERE id = {stepId}" )
            .SingleAsync( ct );
        Assert.AreEqual( expectedString, raw );
    }

    /// <summary>
    /// Verifies that the legacy <c>"Sequential"</c> string value in the database is correctly
    /// read as <see cref="ControlStatement.Default"/> by the converter.
    /// </summary>
    [TestMethod]
    public async Task LegacySequential_ReadsAs_Default( ) {
        CancellationToken ct = TestContext.CancellationToken;

        Workflow workflow = new( ) { Name = "Legacy_WF", Description = "test" };
        _dbContext.Workflows.Add( workflow );
        WerkrTask task = new( ) { Name = "Legacy_Task", ActionType = TaskActionType.ShellCommand, Content = "echo test", TargetTags = ["test"] };
        _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        // Insert a step with "Default" first (to get a valid row)
        WorkflowStep step = new( ) {
            WorkflowId = workflow.Id,
            TaskId = task.Id,
            Order = 0,
            ControlStatement = ControlStatement.Default,
        };
        _dbContext.WorkflowSteps.Add( step );
        _ = await _dbContext.SaveChangesAsync( ct );

        // Manually overwrite the stored string to the legacy "Sequential" value
        long stepId = step.Id;
        _ = await _dbContext.Database.ExecuteSqlAsync(
            $"UPDATE workflow_steps SET control_statement = 'Sequential' WHERE id = {stepId}", ct );

        _dbContext.ChangeTracker.Clear( );

        // Act — reload via EF
        WorkflowStep loaded = await _dbContext.WorkflowSteps.SingleAsync( s => s.Id == step.Id, ct );

        // Assert — converter maps "Sequential" → Default
        Assert.AreEqual( ControlStatement.Default, loaded.ControlStatement );
    }
}
