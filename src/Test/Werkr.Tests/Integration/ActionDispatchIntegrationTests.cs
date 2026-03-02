using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for Action-type task CRUD and validation through the REST API.
/// Validates <c>TaskMapper.ValidateActionFields</c>, parameter deserialization, and
/// the full persistence round-trip for Action tasks.
/// All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class ActionDispatchIntegrationTests {

    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helper Methods

    /// <summary>
    /// Creates an Action-type task via the REST API. Returns the parsed JSON response.
    /// </summary>
    private static async Task<JsonElement> CreateActionTaskAsync(
        string name,
        string actionSubType,
        object parameters,
        CancellationToken ct ) {

        string parametersJson = JsonSerializer.Serialize( parameters, JsonOptions );

        var request = new {
            name,
            description = $"Integration test action task: {name}",
            actionType = "Action",
            content = "",
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

    [TestMethod]
    [Timeout( 60_000 )]
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

    [TestMethod]
    [Timeout( 60_000 )]
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

    [TestMethod]
    [Timeout( 60_000 )]
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
            content = "",
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

    [TestMethod]
    [Timeout( 60_000 )]
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

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task CreateActionTask_MissingActionSubType_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MissingSubType",
            description = "Should fail validation",
            actionType = "Action",
            content = "",
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
        StringAssert.Contains(
            body.GetProperty( "message" ).GetString( )!,
            "ActionSubType",
            "Error message should reference the missing ActionSubType field." );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task CreateActionTask_UnknownActionSubType_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_UnknownSubType",
            description = "Should fail validation",
            actionType = "Action",
            content = "",
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
        StringAssert.Contains(
            body.GetProperty( "message" ).GetString( )!,
            "FlyToMoon",
            "Error message should reference the unknown action name." );
    }

    #endregion Validation — ActionSubType

    #region Validation — ActionParameters

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task CreateActionTask_MissingActionParameters_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MissingParams",
            description = "Should fail validation",
            actionType = "Action",
            content = "",
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
        StringAssert.Contains(
            body.GetProperty( "message" ).GetString( )!,
            "ActionParameters",
            "Error message should reference the missing ActionParameters field." );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task CreateActionTask_MalformedJsonParameters_ReturnsBadRequest( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var request = new {
            name = "IntTest_MalformedJson",
            description = "Should fail validation",
            actionType = "Action",
            content = "",
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

    [TestMethod]
    [Timeout( 60_000 )]
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
        StringAssert.Contains(
            body.GetProperty( "message" ).GetString( )!,
            "ActionSubType",
            "Error message should explain ActionSubType must be null for non-Action tasks." );
    }

    [TestMethod]
    [Timeout( 60_000 )]
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
        StringAssert.Contains(
            body.GetProperty( "message" ).GetString( )!,
            "ActionParameters",
            "Error message should explain ActionParameters must be null for non-Action tasks." );
    }

    #endregion Validation — Non-Action Tasks Reject Action Fields

    #region Ad-Hoc Execution — No Agent

    [TestMethod]
    [Timeout( 60_000 )]
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

        Assert.AreEqual( HttpStatusCode.Conflict, runResponse.StatusCode,
            "Ad-hoc action run should return 409 Conflict when no agent matches the target tags." );

        string body = await runResponse.Content.ReadAsStringAsync( ct );
        StringAssert.Contains( body, "No connected agent",
            "Conflict response should describe that no matching agent was found." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    #endregion Ad-Hoc Execution — No Agent

    #region Job History — New Action Task

    [TestMethod]
    [Timeout( 60_000 )]
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
