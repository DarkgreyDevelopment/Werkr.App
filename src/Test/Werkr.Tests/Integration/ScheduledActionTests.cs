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

    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helpers

    private static async Task<JsonElement> CreateDailyScheduleAsync(
        string name, string date, string time, int dayInterval,
        CancellationToken ct ) {
        var request = new {
            name,
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date, time, timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval },
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/schedules", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Schedule creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    private static async Task<JsonElement> CreateActionTaskAsync(
        string name,
        string actionSubType,
        object parameters,
        Guid? scheduleId,
        CancellationToken ct ) {

        string parametersJson = JsonSerializer.Serialize( parameters, JsonOptions );

        var request = new {
            name,
            description = $"Scheduled action test: {name}",
            actionType = "Action",
            content = "",
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            scheduleId,
            actionSubType,
            actionParameters = parametersJson,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Action task creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    #endregion Helpers

    #region Scheduled Action Task — Create and Link

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ActionTaskLinkedToSchedule_PersistsAllFields( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create a daily schedule
        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ActionSchedule", "2026-07-01", "09:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        // Create an Action task linked to the schedule
        JsonElement task = await CreateActionTaskAsync(
            "IntTest_ScheduledCreateFile",
            "CreateFile",
            new { path = "/data/report.txt", content = "daily report", overwrite = true },
            Guid.Parse( scheduleId ),
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, taskId, "Task ID should be positive." );
        Assert.AreEqual( "Action", task.GetProperty( "actionType" ).GetString( ) );
        Assert.AreEqual( "CreateFile", task.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( scheduleId, task.GetProperty( "scheduleId" ).GetString( ),
            "Task should be linked to the created schedule." );

        // Read back and verify all fields survived the round-trip
        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "CreateFile", retrieved.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( scheduleId, retrieved.GetProperty( "scheduleId" ).GetString( ) );

        string? paramsJson = retrieved.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.AreEqual( "/data/report.txt", parsedParams.RootElement.GetProperty( "path" ).GetString( ) );
        Assert.AreEqual( "daily report", parsedParams.RootElement.GetProperty( "content" ).GetString( ) );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "overwrite" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Scheduled Action Task — Create and Link

    #region Scheduled Action Task — Occurrence Preview

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduledActionTask_OccurrencePreview_ReturnsExpectedDates( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Daily schedule starting Jun 15
        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ActionOccurrence", "2026-06-15", "08:00:00", 2, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        // Link an Action task
        _ = await CreateActionTaskAsync(
            "IntTest_ActionOccurrenceTask",
            "CopyFile",
            new { source = "/data/src", destination = "/data/dst" },
            Guid.Parse( scheduleId ),
            ct );

        // Query occurrence preview for a 10-day window
        string windowEnd = "2026-06-25T23:59:59Z";
        HttpResponseMessage occResponse = await Api.GetAsync(
            $"/api/schedules/{scheduleId}/occurrences?windowEnd={Uri.EscapeDataString( windowEnd )}", ct );
        Assert.AreEqual( HttpStatusCode.OK, occResponse.StatusCode );

        JsonElement occResult = await occResponse.Content
            .ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement occurrences = occResult.GetProperty( "occurrences" );

        // Every 2 days from Jun 15 to Jun 25 → Jun 15, 17, 19, 21, 23, 25 = 6 occurrences
        Assert.AreEqual( 6, occurrences.GetArrayLength( ),
            "Daily schedule (interval=2) from Jun 15 to Jun 25 should produce exactly 6 occurrences." );

        // Verify spacing
        for (int i = 1; i < occurrences.GetArrayLength( ); i++) {
            DateTime prev = DateTime.Parse( occurrences[i - 1].GetString( )! );
            DateTime curr = DateTime.Parse( occurrences[i].GetString( )! );
            double dayDiff = ( curr - prev ).TotalDays;
            Assert.AreEqual( 2.0, dayDiff, 0.01,
                $"Occurrences at index {i - 1} and {i} should be exactly 2 days apart, but gap was {dayDiff:F2} days." );
        }

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Scheduled Action Task — Occurrence Preview

    #region Scheduled Action Task — Update Action Fields

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduledActionTask_UpdateActionType_PersistsChanges( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ActionUpdate", "2026-08-01", "10:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_ScheduledActionUpdate",
            "CreateFile",
            new { path = "/data/original.txt" },
            Guid.Parse( scheduleId ),
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        // Update the task to a different action type while keeping the schedule link
        string newParams = JsonSerializer.Serialize(
            new { path = "/data/original.txt", content = "appended data", append = true },
            JsonOptions );

        var updateRequest = new {
            name = "IntTest_ScheduledActionUpdate_v2",
            description = "Updated to WriteContent",
            actionType = "Action",
            content = "",
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 10L,
            scheduleId = Guid.Parse( scheduleId ),
            actionSubType = "WriteContent",
            actionParameters = newParams,
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/tasks/{taskId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode,
            $"Task update failed: {await putResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "WriteContent", updated.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( scheduleId, updated.GetProperty( "scheduleId" ).GetString( ),
            "Schedule link should be preserved after update." );

        // Verify parameters updated
        string? paramsJson = updated.GetProperty( "actionParameters" ).GetString( );
        Assert.IsNotNull( paramsJson );
        using JsonDocument parsedParams = JsonDocument.Parse( paramsJson );
        Assert.IsTrue( parsedParams.RootElement.GetProperty( "append" ).GetBoolean( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Scheduled Action Task — Update Action Fields

    #region Multiple Action Tasks on Same Schedule

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task MultipleActionTasks_SameSchedule_AllPersistCorrectly( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_MultiAction", "2026-09-01", "06:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;
        Guid scheduleGuid = Guid.Parse( scheduleId );

        // Create three different action tasks on the same schedule
        JsonElement task1 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CreateDir",
            "CreateDirectory",
            new { path = "/data/daily-output" },
            scheduleGuid, ct );

        JsonElement task2 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CreateFile",
            "CreateFile",
            new { path = "/data/daily-output/report.csv", content = "header1,header2" },
            scheduleGuid, ct );

        JsonElement task3 = await CreateActionTaskAsync(
            "IntTest_MultiAction_CopyBackup",
            "CopyFile",
            new { source = "/data/daily-output/report.csv", destination = "/backup/report.csv", overwrite = true },
            scheduleGuid, ct );

        long task1Id = task1.GetProperty( "id" ).GetInt64( );
        long task2Id = task2.GetProperty( "id" ).GetInt64( );
        long task3Id = task3.GetProperty( "id" ).GetInt64( );

        // Verify each task has correct action type and schedule link
        Assert.AreEqual( "CreateDirectory", task1.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( "CreateFile", task2.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( "CopyFile", task3.GetProperty( "actionSubType" ).GetString( ) );

        Assert.AreEqual( scheduleId, task1.GetProperty( "scheduleId" ).GetString( ) );
        Assert.AreEqual( scheduleId, task2.GetProperty( "scheduleId" ).GetString( ) );
        Assert.AreEqual( scheduleId, task3.GetProperty( "scheduleId" ).GetString( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{task1Id}", ct );
        _ = await Api.DeleteAsync( $"/api/tasks/{task2Id}", ct );
        _ = await Api.DeleteAsync( $"/api/tasks/{task3Id}", ct );
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Multiple Action Tasks on Same Schedule

    #region Scheduled Action Task — Unlink Schedule

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduledActionTask_RemoveScheduleLink_TaskRemains( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_UnlinkSchedule", "2026-10-01", "12:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_UnlinkAction",
            "StartProcess",
            new { fileName = "echo", arguments = "scheduled-run", waitForExit = true },
            Guid.Parse( scheduleId ),
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );
        Assert.AreEqual( scheduleId, task.GetProperty( "scheduleId" ).GetString( ) );

        // Update task to remove the schedule link
        string paramsJson = JsonSerializer.Serialize(
            new { fileName = "echo", arguments = "manual-run", waitForExit = true },
            JsonOptions );

        var updateRequest = new {
            name = "IntTest_UnlinkAction",
            description = "No longer scheduled",
            actionType = "Action",
            content = "",
            targetTags = new[] { "integration-test" },
            enabled = true,
            timeoutMinutes = 5L,
            scheduleId = (Guid?) null,
            actionSubType = "StartProcess",
            actionParameters = paramsJson,
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/tasks/{taskId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode,
            $"Task update failed: {await putResponse.Content.ReadAsStringAsync( ct )}" );

        JsonElement updated = await putResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );

        // Schedule link should be removed
        Assert.AreEqual( JsonValueKind.Null, updated.GetProperty( "scheduleId" ).ValueKind,
            "Schedule link should be null after unlinking." );

        // Action fields should be preserved
        Assert.AreEqual( "StartProcess", updated.GetProperty( "actionSubType" ).GetString( ) );
        Assert.AreEqual( "Action", updated.GetProperty( "actionType" ).GetString( ) );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
        _ = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
    }

    #endregion Scheduled Action Task — Unlink Schedule

    #region Schedule Deletion — Action Tasks Persist

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task DeleteSchedule_ActionTasksRetainActionFields( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_DeleteScheduleRetain", "2026-11-01", "08:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateActionTaskAsync(
            "IntTest_RetainedAction",
            "DeleteFile",
            new { path = "/tmp/old-logs.txt", recursive = false, force = true },
            Guid.Parse( scheduleId ),
            ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );

        // Delete the schedule
        HttpResponseMessage deleteResponse = await Api.DeleteAsync( $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResponse.StatusCode );

        // Task should still exist with action fields intact
        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/tasks/{taskId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "Action", retrieved.GetProperty( "actionType" ).GetString( ) );
        Assert.AreEqual( "DeleteFile", retrieved.GetProperty( "actionSubType" ).GetString( ) );
        Assert.IsNotNull( retrieved.GetProperty( "actionParameters" ).GetString( ),
            "ActionParameters should be preserved after schedule deletion." );

        // Cleanup
        _ = await Api.DeleteAsync( $"/api/tasks/{taskId}", ct );
    }

    #endregion Schedule Deletion — Action Tasks Persist
}
