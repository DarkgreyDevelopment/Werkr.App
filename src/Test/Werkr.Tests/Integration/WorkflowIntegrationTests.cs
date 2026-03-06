using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for Workflow, Task, Job, and Schedule endpoints along with Agent and Diagnostics health checks.
/// Organized into regions covering CRUD operations, dependency management, query endpoints,
/// recurrence types, and health/diagnostics.
/// <para>
/// All tests share the <see cref="AppHostFixture"/> instance and use the
/// pre-authenticated API client.
/// </para>
/// </summary>
[TestClass]
public class WorkflowIntegrationTests {

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution, providing access to test
    /// metadata and a <see cref="CancellationToken"/> for cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured with web defaults from <see
    /// cref="AppHostFixture.JsonOptions"/> for JSON serialization and deserialization.
    /// </summary>
    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;

    /// <summary>
    /// Gets the pre-configured, authenticated <see cref="HttpClient"/> from <see cref="AppHostFixture.ApiClient"/> for
    /// sending HTTP requests to the <c>Werkr.Api</c>.
    /// </summary>
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Workflow CRUD

    /// <summary>
    /// Verifies the full CRUD lifecycle for workflows. Creates a new workflow named "IntTest_Workflow", reads it back
    /// and asserts the name matches, updates the name and description, verifies the workflow appears in the list,
    /// deletes the workflow, and confirms a subsequent GET returns <see cref="HttpStatusCode.NotFound"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task WorkflowCrud_CreateReadUpdateDelete( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create
        var createRequest = new {
            name = "IntTest_Workflow",
            description = "Integration test workflow",
            enabled = true,
            scheduleId = (string?) null
        };

        HttpResponseMessage createResponse = await Api.PostAsJsonAsync(
            "/api/workflows", createRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResponse.StatusCode,
            $"Workflow creation failed: {await createResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long workflowId = created.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, workflowId, "Workflow ID should be positive." );
        Assert.AreEqual( "IntTest_Workflow", created.GetProperty( "name" ).GetString( ) );

        // Read
        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/workflows/{workflowId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "IntTest_Workflow", retrieved.GetProperty( "name" ).GetString( ) );
        Assert.AreEqual( "Integration test workflow", retrieved.GetProperty( "description" ).GetString( ) );

        // Update
        var updateRequest = new {
            name = "IntTest_Workflow_Updated",
            description = "Updated description",
            enabled = false,
            scheduleId = (string?) null
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/workflows/{workflowId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode );

        JsonElement updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "IntTest_Workflow_Updated", updated.GetProperty( "name" ).GetString( ) );

        // List
        HttpResponseMessage listResponse = await Api.GetAsync( "/api/workflows", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResponse.StatusCode );

        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.IsGreaterThanOrEqualTo( 1, list.GetArrayLength( ),
            "Workflow list should contain at least one workflow." );

        // Delete
        HttpResponseMessage deleteResponse = await Api.DeleteAsync( $"/api/workflows/{workflowId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResponse.StatusCode );

        HttpResponseMessage notFoundResponse = await Api.GetAsync( $"/api/workflows/{workflowId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, notFoundResponse.StatusCode );
    }

    #endregion Workflow CRUD

    #region Workflow Steps & Dependencies

    /// <summary>
    /// Verifies that workflow steps can be added and linked with dependencies. Creates a workflow and two ShellCommand
    /// tasks, adds each as a step in order, creates a dependency from step 2 to step 1, and asserts the workflow
    /// reports exactly 2 steps. Cleans up the workflow after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task WorkflowSteps_AddAndLinkWithDependency( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create a workflow
        var wfRequest = new {
            name = "IntTest_StepDeps",
            description = "Workflow for step dependency test",
            enabled = true,
            scheduleId = (string?) null
        };

        HttpResponseMessage wfResponse = await Api.PostAsJsonAsync(
            "/api/workflows", wfRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, wfResponse.StatusCode,
            $"Workflow creation failed: {await wfResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement wf = await wfResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long workflowId = wf.GetProperty( "id" ).GetInt64( );

        // Create a task for step 1
        var task1Request = new {
            name = "IntTest_Step1_Task",
            description = "First step task",
            actionType = "ShellCommand",
            content = "echo step1",
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L
        };

        HttpResponseMessage task1Response = await Api.PostAsJsonAsync(
            "/api/tasks", task1Request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, task1Response.StatusCode );

        JsonElement task1 = await task1Response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long task1Id = task1.GetProperty( "id" ).GetInt64( );

        // Create a task for step 2
        var task2Request = new {
            name = "IntTest_Step2_Task",
            description = "Second step task",
            actionType = "ShellCommand",
            content = "echo step2",
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L
        };

        HttpResponseMessage task2Response = await Api.PostAsJsonAsync(
            "/api/tasks", task2Request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, task2Response.StatusCode );

        JsonElement task2 = await task2Response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long task2Id = task2.GetProperty( "id" ).GetInt64( );

        // Add step 1
        var step1Request = new {
            taskId = task1Id,
            order = 1,
            controlStatement = "Sequential",
            dependencyMode = "All"
        };

        HttpResponseMessage step1Response = await Api.PostAsJsonAsync(
            $"/api/workflows/{workflowId}/steps", step1Request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, step1Response.StatusCode,
            $"Step 1 creation failed: {await step1Response.Content.ReadAsStringAsync( ct )}" );

        JsonElement step1 = await step1Response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long step1Id = step1.GetProperty( "id" ).GetInt64( );

        // Add step 2
        var step2Request = new {
            taskId = task2Id,
            order = 2,
            controlStatement = "Sequential",
            dependencyMode = "All"
        };

        HttpResponseMessage step2Response = await Api.PostAsJsonAsync(
            $"/api/workflows/{workflowId}/steps", step2Request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, step2Response.StatusCode,
            $"Step 2 creation failed: {await step2Response.Content.ReadAsStringAsync( ct )}" );

        JsonElement step2 = await step2Response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long step2Id = step2.GetProperty( "id" ).GetInt64( );

        // Add dependency: step 2 depends on step 1
        var depRequest = new { dependsOnStepId = step1Id };

        HttpResponseMessage depResponse = await Api.PostAsJsonAsync(
            $"/api/workflows/{workflowId}/steps/{step2Id}/dependencies", depRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, depResponse.StatusCode,
            $"Dependency creation failed: {await depResponse.Content.ReadAsStringAsync( ct )}" );

        // Verify the workflow now has 2 steps with the dependency
        HttpResponseMessage getWfResponse = await Api.GetAsync( $"/api/workflows/{workflowId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getWfResponse.StatusCode );

        JsonElement fullWorkflow = await getWfResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement steps = fullWorkflow.GetProperty( "steps" );
        Assert.AreEqual( 2, steps.GetArrayLength( ), "Workflow should have exactly 2 steps." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/workflows/{workflowId}", ct );
    }

    #endregion Workflow Steps & Dependencies

    #region Task CRUD with Tags

    /// <summary>
    /// Verifies the full task CRUD lifecycle with target tags. Creates a ShellCommand task with three tags, asserts
    /// the tags persisted, updates the task (changing tags to a single tag), verifies the update, deletes the task,
    /// and confirms a subsequent GET returns <see cref="HttpStatusCode.NotFound"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task TaskCrud_CreateWithTagsUpdateAndDelete( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create task with tags
        var createRequest = new {
            name = "IntTest_TaskTags",
            description = "Task with multiple tags",
            actionType = "ShellCommand",
            content = "echo tagged-task",
            targetTags = new[] { "tag-a", "tag-b", "integration-test" },
            enabled = true,
            timeoutMinutes = 10L
        };

        HttpResponseMessage createResponse = await Api.PostAsJsonAsync(
            "/api/tasks", createRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResponse.StatusCode,
            $"Task creation failed: {await createResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        long taskId = created.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, taskId );

        // Verify tags persisted
        JsonElement tags = created.GetProperty( "targetTags" );
        Assert.AreEqual( 3, tags.GetArrayLength( ), "Task should have 3 target tags." );

        // Update task
        var updateRequest = new {
            name = "IntTest_TaskTags_Updated",
            description = "Updated task",
            actionType = "ShellCommand",
            content = "echo updated-task",
            targetTags = new[] { "tag-c" },
            enabled = false,
            timeoutMinutes = 15L
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/tasks/{taskId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode );

        JsonElement updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "IntTest_TaskTags_Updated", updated.GetProperty( "name" ).GetString( ) );

        // Verify tags updated
        JsonElement updatedTags = updated.GetProperty( "targetTags" );
        Assert.AreEqual( 1, updatedTags.GetArrayLength( ), "Updated task should have 1 target tag." );

        // Delete
        HttpResponseMessage deleteResponse = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResponse.StatusCode );

        HttpResponseMessage notFoundResponse = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, notFoundResponse.StatusCode );
    }

    #endregion Task CRUD with Tags

    #region Job Query Endpoints

    /// <summary>
    /// Verifies that the <c>GET /api/jobs</c> endpoint returns a JSON array. Also tests the date-range query filter
    /// (<c>?since=…&amp;until=…</c>) for a historical range, expecting no results.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task JobListEndpoint_ReturnsFilterableResults( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.GetAsync( "/api/jobs", ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement jobs = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( JsonValueKind.Array, jobs.ValueKind,
            "Job list should return a JSON array." );

        // Test with date filter (should still return OK even if no jobs match)
        HttpResponseMessage filteredResponse = await Api.GetAsync(
            "/api/jobs?since=2020-01-01T00:00:00Z&until=2020-01-02T00:00:00Z", ct );
        Assert.AreEqual( HttpStatusCode.OK, filteredResponse.StatusCode );

        JsonElement filtered = await filteredResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( 0, filtered.GetArrayLength( ),
            "No jobs should exist in the 2020 date range." );
    }

    /// <summary>
    /// Verifies that requesting a non-existent job by a random GUID returns <see cref="HttpStatusCode.NotFound"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task GetJobById_ForNonExistentJob_ReturnsNotFound( ) {
        CancellationToken ct = TestContext.CancellationToken;

        Guid fakeJobId = Guid.NewGuid( );
        HttpResponseMessage response = await Api.GetAsync( $"/api/jobs/{fakeJobId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );
    }

    #endregion Job Query Endpoints

    #region Schedule Recurrence Types

    /// <summary>
    /// Verifies that creating a weekly schedule with Monday, Wednesday, and Friday recurrence persists correctly.
    /// Creates the schedule, reads it back, and asserts the <c>weeklyRecurrence.weekInterval</c> is 1 and the
    /// <c>daysOfWeek</c> flags value equals 21 (Monday=1 | Wednesday=4 | Friday=16).
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task WeeklySchedule_CreatesAndReturnsCorrectRecurrence( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Monday=1, Wednesday=4, Friday=16 → flags value = 21
        var request = new {
            name = "IntTest_WeeklySchedule",
            stopTaskAfterMinutes = 30L,
            startDateTime = new { date = "2026-06-15", time = "09:00:00", timeZoneId = "UTC" },
            weeklyRecurrence = new {
                weekInterval = 1,
                daysOfWeek = 1 | 4 | 16   // Monday | Wednesday | Friday
            }
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/schedules", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Weekly schedule creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string scheduleId = created.GetProperty( "id" ).GetString( )!;
        Assert.IsFalse( string.IsNullOrEmpty( scheduleId ) );

        // Verify weekly recurrence persisted
        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement weekly = retrieved.GetProperty( "weeklyRecurrence" );
        Assert.AreEqual( 1, weekly.GetProperty( "weekInterval" ).GetInt32( ) );

        int days = weekly.GetProperty( "daysOfWeek" ).GetInt32( );
        Assert.AreEqual( 1 | 4 | 16, days, "DaysOfWeek flags should be Monday|Wednesday|Friday (21)." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Schedule Recurrence Types

    #region Agent Health & Diagnostics

    /// <summary>
    /// Verifies that the <c>GET /api/agents/health</c> endpoint returns HTTP 200 OK and a JSON array.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task AgentHealthEndpoint_ReturnsSuccessfully( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.GetAsync( "/api/agents/health", ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement health = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( JsonValueKind.Array, health.ValueKind,
            "Agent health should return a JSON array." );
    }

    /// <summary>
    /// Verifies that the <c>GET /api/diagnostics/health</c> endpoint returns HTTP 200 OK with a JSON array containing
    /// at least one database health entry.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000 )]
    public async Task DiagnosticsHealthEndpoint_ReturnsDatabaseStatus( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.GetAsync( "/api/diagnostics/health", ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        JsonElement diagnostics = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( JsonValueKind.Array, diagnostics.ValueKind );
        Assert.IsGreaterThanOrEqualTo( 1, diagnostics.GetArrayLength( ),
            "Should return at least one database health entry." );
    }

    #endregion Agent Health & Diagnostics
}
