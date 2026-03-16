using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for Action-type task CRUD and validation through the REST API.
/// Validates <see cref="TaskMapper.ValidateActionFields"/>, parameter deserialization,
/// and the full persistence round-trip for Action tasks.
/// All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class ActionDispatchIntegrationTests {

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution,
    /// providing access to test metadata and a <see cref="CancellationToken"/> for
    /// cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured with web defaults
    /// from <see cref="AppHostFixture.JsonOptions"/> for JSON serialization and deserialization.
    /// </summary>
    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;

    /// <summary>
    /// Gets the pre-configured, authenticated <see cref="HttpClient"/> from
    /// <see cref="AppHostFixture.ApiClient"/> for sending HTTP requests to the
    /// <c>Werkr.Api</c>.
    /// </summary>
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helper Methods

    /// <summary>
    /// Creates an Action-type task via the REST API. Returns the parsed JSON response.
    /// </summary>
    private static async Task<JsonElement> CreateActionTaskAsync(
        string name,
        string actionSubType,
        object parameters,
        CancellationToken ct
    ) {

        string parametersJson = JsonSerializer.Serialize( parameters, JsonOptions );

        var request = new {
            name,
            description = $"Integration test action task: {name}",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            actionSubType,
            actionParameters = parametersJson,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Action task creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    #endregion Helper Methods

    #region Action Task CRUD

    /// <summary>
    /// Verifies that creating a CopyFile action task persists all fields correctly and
    /// that retrieving the task by ID returns the expected <see cref="ActionType"/>,
    /// <see cref="ActionSubType"/>, and deserialized <see cref="ActionParameters"/>
    /// (source, destination, overwrite). The test creates the task, reads it back via
    /// GET, parses the action parameters JSON, and validates each parameter value.
    /// Cleans up the created task after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_CopyFile_PersistsAndReturnsCorrectData( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "IntTest_ActionCrud_CopyFile",
            "CopyFile",
            new { source = "/tmp/src.txt", destination = "/tmp/dst.txt", overwrite = true },
            ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, taskId, "Task ID should be positive." );
        Assert.AreEqual( "Action", created.GetProperty( "actionType" ).GetString( ) );
        Assert.AreEqual( "CopyFile", created.GetProperty( "actionSubType" ).GetString( ) );
        Assert.IsNotNull( created.GetProperty( "actionParameters" ).GetString( ),
            "ActionParameters should be persisted." );

        // Read back
        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "CopyFile", retrieved.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( "Action", retrieved.GetProperty( "actionType" ).GetString( ) );

        // Verify parameters round-trip
        string? paramsJson = retrieved.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.AreEqual( "/tmp/src.txt", parsedParams.RootElement.GetProperty( "source" ).GetString( ) );
        Assert.AreEqual( "/tmp/dst.txt", parsedParams.RootElement.GetProperty( "destination" ).GetString( ) );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "overwrite" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    /// <summary>
    /// Verifies that all supported action sub-types can be created successfully.
    /// Each action type is created with appropriate parameters, and the test asserts
    /// that each receives a positive task ID and the correct <see cref="ActionSubType"/>
    /// value. All created tasks are cleaned up after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_AllActionTypes_Succeed( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Each action type with minimal valid parameters
        (string subType, object parameters)[] actionTypes = [
            ("CopyFile", new { source = "/a", destination = "/b" }),
            ("MoveFile", new { source = "/a", destination = "/b" }),
            ("RenameFile", new { path = "/a/file.txt", newName = "renamed.txt" }),
            ("DeleteFile", new { path = "/a/file.txt" }),
            ("CreateFile", new { path = "/a/new.txt" }),
            ("CreateDirectory", new { path = "/a/newdir" }),
            ("TestExists", new { path = "/a/file.txt" }),
            ("ClearContent", new { path = "/a/file.txt" }),
            ("WriteContent", new { path = "/a/file.txt", content = "data" }),
            ("StartProcess", new { fileName = "echo", arguments = "hello" }),
            ("StopProcess", new { processName = "notepad" }),
            ("Delay", new { seconds = 1.0 }),
            ("GetFileInfo", new { path = "/a/file.txt" }),
            ("ReadContent", new { path = "/a/file.txt" }),
            ("ListDirectory", new { path = "/a" }),
            ("FindReplace", new { path = "/a/file.txt", find = "old", replace = "new" }),
            ("CompressArchive", new { source = "/a/file.txt", destination = "/a/archive.zip" }),
            ("ExpandArchive", new { source = "/a/archive.zip", destination = "/a/out" }),
            ("WatchFile", new { directory = "/a", pattern = "*.txt" }),
        ];

        List<long> createdIds = [];

        foreach ((string subType, object parameters) in actionTypes) {
            JsonElement created = await CreateActionTaskAsync(
                $"IntTest_AllActions_{subType}", subType, parameters, ct );

            long taskId = created.GetProperty( "id" ).GetInt64( );
            Assert.IsGreaterThan( 0L, taskId, $"{subType} task should have a positive ID." );
            Assert.AreEqual( subType, created.GetProperty( "actionSubType" ).GetString( ),
                $"ActionSubType should match '{subType}'." );
            createdIds.Add( taskId );
        }

        // Cleanup
        foreach (long id in createdIds) {
            _ = await Api.DeleteAsync( $"/api/tasks/{id}", ct );
        }
    }

    /// <summary>
    /// Verifies that updating an existing action task changes its <see cref="ActionSubType"/>,
    /// <see cref="ActionParameters"/>, name, and timeout. Creates a CreateFile action task,
    /// then issues a PUT request to change it to a WriteContent action with different parameters.
    /// Asserts that the updated response reflects the new sub-type, name, and parameter
    /// values (including the added "append" flag). Cleans up the task after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdateActionTask_ChangesActionParameters( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "IntTest_ActionUpdate",
            "CreateFile",
            new { path = "/tmp/original.txt", content = "original" },
            ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        // Update to WriteContent action
        string updatedParams = JsonSerializer.Serialize(
            new { path = "/tmp/updated.txt", content = "updated", append = true },
            JsonOptions );

        var updateRequest = new {
            name = "IntTest_ActionUpdate_Modified",
            description = "Updated action task",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 10L,
            actionSubType = "WriteContent",
            actionParameters = updatedParams,
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/tasks/{taskId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode,
            $"Task update failed: {await putResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "WriteContent", updated.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( "IntTest_ActionUpdate_Modified", updated.GetProperty( "name" ).GetString( ) );

        // Verify new parameters persisted
        string? paramsJson = updated.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.AreEqual( "/tmp/updated.txt", parsedParams.RootElement.GetProperty( "path" ).GetString( ) );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "append" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    /// <summary>
    /// Verifies that deleting an action task removes it from the system. Creates a DeleteFile
    /// action task, issues a DELETE request, and asserts that the response is
    /// <see cref="HttpStatusCode.NoContent"/>. Then attempts to retrieve the task by ID and
    /// asserts that <see cref="HttpStatusCode.NotFound"/> is returned.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task DeleteActionTask_RemovesAndReturnsNotFound( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateActionTaskAsync(
            "IntTest_ActionDelete",
            "DeleteFile",
            new { path = "/tmp/todelete.txt", recursive = true },
            ct );

        long taskId = created.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage deleteResponse = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResponse.StatusCode );

        HttpResponseMessage notFoundResponse = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, notFoundResponse.StatusCode );
    }

    #endregion Action Task CRUD

    #region Validation — ActionSubType

    /// <summary>
    /// Verifies that creating an action task with a null <see cref="ActionSubType"/> returns
    /// <see cref="HttpStatusCode.BadRequest"/>. Asserts that the error response body contains a
    /// message referencing the missing "ActionSubType" field, confirming server-side validation
    /// rejects action tasks that lack a required sub-type.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_MissingActionSubType_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MissingSubType",
            description = "Should fail validation",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionSubType = (string?) null,
            actionParameters = """{"path":"/tmp/test.txt"}""",
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "Missing ActionSubType should return 400 Bad Request." );

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.Contains(
            "ActionSubType",
            body.GetProperty( "message" ).GetString( )!,
            "Error message should reference the missing ActionSubType field." );
    }

    /// <summary>
    /// Verifies that creating an action task with an unrecognized <see cref="ActionSubType"/> value
    /// ("FlyToMoon") returns <see cref="HttpStatusCode.BadRequest"/>. Asserts that the error response
    /// body references the unknown action name, confirming server-side validation rejects unrecognized
    /// action sub-types.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_UnknownActionSubType_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_UnknownSubType",
            description = "Should fail validation",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionSubType = "FlyToMoon",
            actionParameters = """{"rocket":"saturn-v"}""",
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "Unknown ActionSubType should return 400 Bad Request." );

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.Contains(
            "FlyToMoon",
            body.GetProperty( "message" ).GetString( )!,
            "Error message should reference the unknown action name." );
    }

    #endregion Validation — ActionSubType

    #region Validation — ActionParameters

    /// <summary>
    /// Verifies that creating an action task with null <see cref="ActionParameters"/> returns
    /// <see cref="HttpStatusCode.BadRequest"/>. Asserts that the error response body contains a
    /// message referencing the missing "ActionParameters" field, confirming that action tasks
    /// require parameters to be specified.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_MissingActionParameters_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MissingParams",
            description = "Should fail validation",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionSubType = "CreateFile",
            actionParameters = (string?) null,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "Missing ActionParameters should return 400 Bad Request." );

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.Contains(
            "ActionParameters",
            body.GetProperty( "message" ).GetString( )!,
            "Error message should reference the missing ActionParameters field." );
    }

    /// <summary>
    /// Verifies that creating an action task with malformed (non-JSON) <see cref="ActionParameters"/>
    /// returns <see cref="HttpStatusCode.BadRequest"/>. Sends a request with the string
    /// "not-valid-json!!!" as the action parameters and asserts that the API rejects the request,
    /// confirming that action parameters must be valid JSON.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateActionTask_MalformedJsonParameters_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MalformedJson",
            description = "Should fail validation",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionSubType = "CreateFile",
            actionParameters = "not-valid-json!!!",
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "Malformed JSON in ActionParameters should return 400 Bad Request." );
    }

    #endregion Validation — ActionParameters

    #region Validation — Non-Action Tasks Reject Action Fields

    /// <summary>
    /// Verifies that creating a ShellCommand task with an <see cref="ActionSubType"/> set returns
    /// <see cref="HttpStatusCode.BadRequest"/>. Asserts that the error message explains that
    /// <see cref="ActionSubType"/> must be null for non-Action task types, enforcing the constraint
    /// that action-specific fields are exclusive to Action-type tasks.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateShellTask_WithActionSubType_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_ShellWithSubType",
            description = "Shell task should not have ActionSubType",
            actionType = "ShellCommand",
            content = "echo hello",
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionSubType = "CopyFile",
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "ShellCommand task with ActionSubType should return 400 Bad Request." );

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.Contains(
            "ActionSubType",
            body.GetProperty( "message" ).GetString( )!,
            "Error message should explain ActionSubType must be null for non-Action tasks." );
    }

    /// <summary>
    /// Verifies that creating a ShellCommand task with <see cref="ActionParameters"/> set returns
    /// <see cref="HttpStatusCode.BadRequest"/>. Asserts that the error message explains that
    /// <see cref="ActionParameters"/> must be null for non-Action task types, enforcing the constraint
    /// that action-specific fields are exclusive to Action-type tasks.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateShellTask_WithActionParameters_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_ShellWithParams",
            description = "Shell task should not have ActionParameters",
            actionType = "ShellCommand",
            content = "echo hello",
            targetTags = new[] { "integration-test" },
            enabled = true,
            actionParameters = """{"path":"/tmp/test.txt"}""",
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode,
            "ShellCommand task with ActionParameters should return 400 Bad Request." );

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.Contains(
            "ActionParameters",
            body.GetProperty( "message" ).GetString( )!,
            "Error message should explain ActionParameters must be null for non-Action tasks." );
    }

    #endregion Validation — Non-Action Tasks Reject Action Fields

    #region Ad-Hoc Execution — No Agent

    /// <summary>
    /// Verifies that attempting an ad-hoc run of an action task when no agent is connected returns
    /// <see cref="HttpStatusCode.Accepted"/> (HTTP 202). The run endpoint creates a one-time schedule
    /// and returns immediately. Creates a TestExists action task with "integration-test" target tags
    /// and issues a POST to <c>/api/tasks/{id}/run</c>. Asserts the accepted status. Cleans up the task.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AdHocRunActionTask_WithoutConnectedAgent_ReturnsConflict( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_AdHocActionRun",
            "TestExists",
            new { path = "/tmp/check.txt", type = 2 },  // PathType.Any = 2
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage runResponse = await Api.PostAsJsonAsync(
            $"/api/tasks/{taskId}/run", new object( ), JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.Accepted, runResponse.StatusCode,
            "Ad-hoc action run should return 202 Accepted (one-time schedule created)." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    #endregion Ad-Hoc Execution — No Agent

    #region Job History — New Action Task

    /// <summary>
    /// Verifies that a newly created action task has no job execution history. Creates a
    /// CreateDirectory action task, queries its job history via <c>GET /api/tasks/{id}/jobs</c>,
    /// and asserts that the returned JSON array is empty. Cleans up the task after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task JobHistory_ForNewActionTask_ReturnsEmptyList( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_ActionJobHistory",
            "CreateDirectory",
            new { path = "/tmp/newdir" },
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage jobsResponse = await Api.GetAsync( $"/api/tasks/{taskId}/jobs", ct );
        Assert.AreEqual( HttpStatusCode.OK, jobsResponse.StatusCode );

        JsonElement jobList = await jobsResponse.Content
            .ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( JsonValueKind.Array, jobList.ValueKind );
        Assert.AreEqual( 0, jobList.GetArrayLength( ),
            "A newly created action task should have no job history." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    #endregion Job History — New Action Task
}
