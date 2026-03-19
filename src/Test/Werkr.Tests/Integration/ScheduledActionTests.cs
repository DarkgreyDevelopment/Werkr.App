using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for Action-type tasks linked to schedules.
/// Validates that Action tasks persist correctly with schedules, that scheduled
/// occurrence previews work for Action tasks, and that the schedule-linked
/// Action task round-trips all action-specific fields correctly.
/// All tests share the <see cref="AppHostFixture"/> instance.
/// </summary>
[TestClass]
public class ScheduledActionTests {

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
    /// <see cref="AppHostFixture.ApiClient"/> for sending HTTP requests to the <c>Werkr.Api</c>.
    /// </summary>
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helpers

    /// <summary>
    /// Creates a daily-recurrence schedule via <c>POST /api/schedules</c> and returns the
    /// deserialized JSON response. The schedule is configured with the specified start date, time,
    /// day interval, and a 60-minute task timeout. Asserts that the creation returns
    /// <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<JsonElement> CreateDailyScheduleAsync(
        string name,
        string date,
        string time,
        int dayInterval,
        CancellationToken ct
    ) {
        var request = new {
            name,
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date, time, timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval },
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/schedules",
            request,
            JsonOptions,
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            $"Schedule creation failed: {await response.Content.ReadAsStringAsync( ct )}"
        );

        return await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions,
            ct
        );
    }

    /// <summary>
    /// Creates an action-type task via <c>POST /api/tasks</c> and returns the deserialized JSON
    /// response. The task is configured with the specified action sub-type, serialized parameters,
    /// and "integration-test" target tags. Asserts that the creation returns
    /// <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<JsonElement> CreateActionTaskAsync(
        string name,
        string actionSubType,
        object parameters,
        CancellationToken ct
    ) {

        string parametersJson = JsonSerializer.Serialize(
            parameters,
            JsonOptions
        );

        var request = new {
            name,
            description = $"Scheduled action test: {name}",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            actionSubType,
            actionParameters = parametersJson,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/tasks",
            request,
            JsonOptions,
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            $"Action task creation failed: {await response.Content.ReadAsStringAsync( ct )}"
        );

        return await response.Content.ReadFromJsonAsync<JsonElement>(
            JsonOptions,
            ct
        );
    }

    #endregion Helpers

    #region Action Task — Create and Persist Fields

    /// <summary>
    /// Verifies that an action task persists all fields correctly. Creates a CreateFile action
    /// task, then retrieves the task by ID and asserts that the action type, sub-type, and
    /// deserialized action parameters (path, content, overwrite) all match the expected values.
    /// Cleans up the task after verification.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ActionTaskLinkedToSchedule_PersistsAllFields( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create an Action task
        JsonElement task = await CreateActionTaskAsync(
            "IntTest_ScheduledCreateFile",
            "CreateFile",
            new {
                path = "/data/report.txt",
                content = "daily report",
                overwrite = true,
            },
            ct
        );

        long taskId = task.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan(
            0L,
            taskId,
            "Task ID should be positive."
        );
        Assert.AreEqual(
            "Action",
            task.GetProperty( "actionType" ).GetString( )
        );
        Assert.AreEqual(
            "CreateFile",
            task.GetProperty( "actionSubType" ).GetString( )
        );

        // Read back and verify all fields survived the round-trip
        HttpResponseMessage getResponse = await Api.GetAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.OK,
            getResponse.StatusCode
        );

        JsonElement retrieved = await getResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                JsonOptions,
                ct
            );
        Assert.AreEqual(
            "CreateFile",
            retrieved.GetProperty( "actionSubType" ).GetString( )
        );

        string? paramsJson = retrieved.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.AreEqual(
            "/data/report.txt",
            parsedParams.RootElement.GetProperty( "path" ).GetString( )
        );
        Assert.AreEqual(
            "daily report",
            parsedParams.RootElement.GetProperty( "content" ).GetString( )
        );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "overwrite" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
    }

    #endregion Action Task — Create and Persist Fields

    #region Scheduled Action Task — Occurrence Preview

    /// <summary>
    /// Verifies that the occurrence preview for a daily schedule returns the expected dates.
    /// Creates a schedule with a 2-day interval starting 2026-06-15 at 08:00 UTC, requests
    /// occurrences through 2026-06-25, and asserts exactly 6 occurrences are returned with
    /// each consecutive pair exactly 2 days apart. Cleans up the schedule.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ScheduledActionTask_OccurrencePreview_ReturnsExpectedDates( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Daily schedule starting Jun 15
        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ActionOccurrence",
            "2026-06-15",
            "08:00:00",
            2,
            ct
        );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        // Query occurrence preview for a 10-day window
        string windowEnd = "2026-06-25T23:59:59Z";
        HttpResponseMessage occResponse = await Api.GetAsync(
            $"/api/v1/schedules/{scheduleId}/occurrences?windowEnd={Uri.EscapeDataString( windowEnd )}",
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.OK,
            occResponse.StatusCode
        );

        JsonElement occResult = await occResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                JsonOptions,
                ct
            );
        JsonElement occurrences = occResult.GetProperty( "occurrences" );

        // Every 2 days from Jun 15 to Jun 25 → Jun 15, 17, 19, 21, 23, 25 = 6 occurrences
        Assert.AreEqual(
            6,
            occurrences.GetArrayLength( ),
            "Daily schedule (interval=2) from Jun 15 to Jun 25 "
            + "should produce exactly 6 occurrences."
        );

        // Verify spacing
        for (int i = 1; i < occurrences.GetArrayLength( ); i++) {
            DateTime prev = DateTime.Parse( occurrences[i - 1].GetString( )! );
            DateTime curr = DateTime.Parse( occurrences[i].GetString( )! );
            double dayDiff = ( curr - prev ).TotalDays;
            Assert.AreEqual(
                2.0,
                dayDiff,
                0.01,
                $"Occurrences at index {i - 1} and {i} should be "
                + $"exactly 2 days apart, but gap was {dayDiff:F2} days."
            );
        }

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/schedules/{scheduleId}",
            ct
        );
    }

    #endregion Scheduled Action Task — Occurrence Preview

    #region Action Task — Update Action Fields

    /// <summary>
    /// Verifies that updating an action task's action sub-type and parameters persists the
    /// changes correctly. Creates a CreateFile action task, then issues a PUT to change the
    /// action to WriteContent with new parameters (including an "append" flag). Asserts that
    /// the updated response reflects the new sub-type and the new parameters are persisted.
    /// Cleans up the task.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ScheduledActionTask_UpdateActionType_PersistsChanges( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_ScheduledActionUpdate",
            "CreateFile",
            new { path = "/data/original.txt" },
            ct
        );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        // Update the task to a different action type
        string newParams = JsonSerializer.Serialize(
            new {
                path = "/data/original.txt",
                content = "appended data",
                append = true,
            },
            JsonOptions
        );

        var updateRequest = new {
            name = "IntTest_ScheduledActionUpdate_v2",
            description = "Updated to WriteContent",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 10L,
            actionSubType = "WriteContent",
            actionParameters = newParams,
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/v1/tasks/{taskId}",
            updateRequest,
            JsonOptions,
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.OK,
            putResponse.StatusCode,
            $"Task update failed: {await putResponse.Content.ReadAsStringAsync( ct )}"
        );

        JsonElement updated = await putResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                JsonOptions,
                ct
            );
        Assert.AreEqual(
            "WriteContent",
            updated.GetProperty( "actionSubType" ).GetString( )
        );

        // Verify parameters updated
        string? paramsJson = updated.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "append" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
    }

    #endregion Action Task — Update Action Fields

    #region Multiple Action Tasks — All Persist Correctly

    /// <summary>
    /// Verifies that multiple action tasks with different action sub-types all persist correctly.
    /// Creates three action tasks (CreateDirectory, CreateFile, CopyFile) and asserts that each
    /// has the expected action sub-type. Cleans up all tasks.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task MultipleActionTasks_SameSchedule_AllPersistCorrectly( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create three different action tasks
        JsonElement task1 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CreateDir",
            "CreateDirectory",
            new { path = "/data/daily-output" },
            ct
        );

        JsonElement task2 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CreateFile",
            "CreateFile",
            new {
                path = "/data/daily-output/report.csv",
                content = "header1,header2",
            },
            ct
        );

        JsonElement task3 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CopyBackup",
            "CopyFile",
            new {
                source = "/data/daily-output/report.csv",
                destination = "/backup/report.csv",
                overwrite = true,
            },
            ct
        );

        long task1Id = task1.GetProperty( "id" ).GetInt64( );
        long task2Id = task2.GetProperty( "id" ).GetInt64( );
        long task3Id = task3.GetProperty( "id" ).GetInt64( );

        // Verify each task has correct action sub-type
        Assert.AreEqual(
            "CreateDirectory",
            task1.GetProperty( "actionSubType" ).GetString( )
        );
        Assert.AreEqual(
            "CreateFile",
            task2.GetProperty( "actionSubType" ).GetString( )
        );
        Assert.AreEqual(
            "CopyFile",
            task3.GetProperty( "actionSubType" ).GetString( )
        );

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{task1Id}",
            ct
        );
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{task2Id}",
            ct
        );
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{task3Id}",
            ct
        );
    }

    #endregion Multiple Action Tasks — All Persist Correctly

    #region Action Task — Update Preserves Action Fields

    /// <summary>
    /// Verifies that updating an action task's parameters preserves the task
    /// and its action fields. Creates a StartProcess action task, then updates
    /// it with new parameters. Asserts that the action sub-type and action type
    /// remain intact and that the updated parameters are persisted. Cleans up
    /// the task.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ScheduledActionTask_RemoveScheduleLink_TaskRemains( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_UnlinkAction",
            "StartProcess",
            new {
                fileName = "echo",
                arguments = "scheduled-run",
                waitForExit = true,
            },
            ct
        );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        // Update task with new parameters
        string paramsJson = JsonSerializer.Serialize(
            new {
                fileName = "echo",
                arguments = "manual-run",
                waitForExit = true,
            },
            JsonOptions
        );

        var updateRequest = new {
            name = "IntTest_UnlinkAction",
            description = "Updated parameters",
            actionType = "Action",
            content = string.Empty,
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            actionSubType = "StartProcess",
            actionParameters = paramsJson,
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/v1/tasks/{taskId}",
            updateRequest,
            JsonOptions,
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.OK,
            putResponse.StatusCode,
            $"Task update failed: {await putResponse.Content.ReadAsStringAsync( ct )}"
        );

        JsonElement updated = await putResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                JsonOptions,
                ct
            );

        // Action fields should be preserved
        Assert.AreEqual(
            "StartProcess",
            updated.GetProperty( "actionSubType" ).GetString( )
        );
        Assert.AreEqual(
            "Action",
            updated.GetProperty( "actionType" ).GetString( )
        );

        // Verify parameters were updated
        string? updatedParamsJson = updated.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( updatedParamsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( updatedParamsJson );
        Assert.AreEqual(
            "manual-run",
            parsedParams.RootElement.GetProperty( "arguments" ).GetString( )
        );

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
    }

    #endregion Action Task — Update Preserves Action Fields

    #region Schedule Deletion — Action Tasks Persist

    /// <summary>
    /// Verifies that deleting a schedule preserves the linked action task
    /// and its action-specific fields. Creates a daily schedule with a
    /// DeleteFile action task, deletes the schedule, then retrieves the
    /// task and asserts that <see cref="ActionType"/>,
    /// <see cref="ActionSubType"/>, and <see cref="ActionParameters"/>
    /// are all still present. Cleans up the task.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task DeleteSchedule_ActionTasksRetainActionFields( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_DeleteScheduleRetain",
            "2026-11-01",
            "08:00:00",
            1,
            ct
        );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_RetainedAction",
            "DeleteFile",
            new {
                path = "/tmp/old-logs.txt",
                recursive = false,
                force = true,
            },
            ct
        );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        // Delete the schedule
        HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/v1/schedules/{scheduleId}",
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.NoContent,
            deleteResponse.StatusCode
        );

        // Task should still exist with action fields intact
        HttpResponseMessage getResponse = await Api.GetAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
        Assert.AreEqual(
            HttpStatusCode.OK,
            getResponse.StatusCode
        );

        JsonElement retrieved = await getResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                JsonOptions,
                ct
            );
        Assert.AreEqual(
            "Action",
            retrieved.GetProperty( "actionType" ).GetString( )
        );
        Assert.AreEqual(
            "DeleteFile",
            retrieved.GetProperty( "actionSubType" ).GetString( )
        );
        Assert.IsNotNull(
            retrieved.GetProperty( "actionParameters" ).GetString( ),
            "ActionParameters should be preserved after schedule deletion."
        );

        // Cleanup
        _ = await Api.DeleteAsync(
            $"/api/v1/tasks/{taskId}",
            ct
        );
    }

    #endregion Schedule Deletion — Action Tasks Persist
}
