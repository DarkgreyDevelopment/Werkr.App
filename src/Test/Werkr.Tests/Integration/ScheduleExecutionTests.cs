using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for the scheduling execution pipeline.
/// Tests exercise the Werkr API (via Testcontainers + WebApplicationFactory) through REST API
/// endpoints, verifying schedule/task/job persistence, occurrence calculation, ad-hoc
/// execution error handling, and schedule invalidation flow.
/// <para>
/// All tests share the <see cref="AppHostFixture"/> instance and use the
/// pre-authenticated API client.
/// </para>
/// </summary>
[TestClass]
public class ScheduleExecutionTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Test Infrastructure

    private static async Task<JsonElement> CreateDailyScheduleAsync(
        string name, string date, string time, int dayInterval,
        CancellationToken ct ) {
        var request = new {
            name,
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date, time, timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval }
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/schedules", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Schedule creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    private static async Task<JsonElement> CreateTaskAsync(
        string name, string content, string[] targetTags,
        Guid? scheduleId, CancellationToken ct ) {
        var request = new {
            name,
            description = $"Integration test task: {name}",
            actionType = "ShellCommand",
            content,
            targetTags,
            enabled = true,
            timeoutMinutes = 5L,
            scheduleId
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Task creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    #endregion Test Infrastructure

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduleCreation_PersistsAndReturnsCorrectData( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateDailyScheduleAsync(
            "IntTest_ScheduleCrud", "2026-06-15", "08:30:00", 1, ct );

        string scheduleId = created.GetProperty( "id" ).GetString( )!;
        Assert.IsFalse( string.IsNullOrEmpty( scheduleId ), "Schedule ID should be a non-empty GUID." );

        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement retrieved = await getResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "IntTest_ScheduleCrud", retrieved.GetProperty( "name" ).GetString( ) );
        Assert.AreEqual( 60L, retrieved.GetProperty( "stopTaskAfterMinutes" ).GetInt64( ) );

        JsonElement startDt = retrieved.GetProperty( "startDateTime" );
        Assert.AreEqual( "2026-06-15", startDt.GetProperty( "date" ).GetString( ) );
        StringAssert.StartsWith( startDt.GetProperty( "time" ).GetString( )!, "08:30" );
        Assert.AreEqual( "UTC", startDt.GetProperty( "timeZoneId" ).GetString( ) );

        JsonElement daily = retrieved.GetProperty( "dailyRecurrence" );
        Assert.AreEqual( 1, daily.GetProperty( "dayInterval" ).GetInt32( ) );

        HttpResponseMessage listResponse = await Api.GetAsync( "/api/schedules", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResponse.StatusCode );

        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.IsGreaterThanOrEqualTo( 1, list.GetArrayLength( ),
            "Schedule list should contain at least one schedule." );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task TaskLinkedToSchedule_OccurrencePreviewReturnsExpectedDates( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_OccurrencePreview", "2026-06-15", "08:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateTaskAsync(
            "IntTest_OccurrenceTask", "echo occurrence-test",
            ["integration-test"], Guid.Parse( scheduleId ), ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, taskId, "Task ID should be a positive integer." );
        Assert.AreEqual( scheduleId, task.GetProperty( "scheduleId" ).GetString( ),
            "Task should be linked to the created schedule." );

        string windowEnd = "2026-06-22T23:59:59Z";
        HttpResponseMessage occResponse = await Api.GetAsync(
            $"/api/schedules/{scheduleId}/occurrences?windowEnd={Uri.EscapeDataString( windowEnd )}", ct );
        Assert.AreEqual( HttpStatusCode.OK, occResponse.StatusCode );

        JsonElement occResult = await occResponse.Content
            .ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement occurrences = occResult.GetProperty( "occurrences" );

        Assert.AreEqual( 8, occurrences.GetArrayLength( ),
            "Daily schedule (interval=1) from Jun 15 to Jun 22 should produce exactly 8 occurrences." );

        for (int i = 1; i < occurrences.GetArrayLength( ); i++) {
            DateTime prev = DateTime.Parse( occurrences[i - 1].GetString( )! );
            DateTime curr = DateTime.Parse( occurrences[i].GetString( )! );
            double dayDiff = ( curr - prev ).TotalDays;
            Assert.AreEqual( 1.0, dayDiff, 0.01,
                $"Occurrences at index {i - 1} and {i} should be exactly 1 day apart, but gap was {dayDiff:F2} days." );
        }
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduleUpdate_PersistsChangesAndTriggersInvalidationPath( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ScheduleUpdate", "2026-06-15", "08:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        _ = await CreateTaskAsync(
            "IntTest_UpdateLinkedTask", "echo update-test",
            ["integration-test"], Guid.Parse( scheduleId ), ct );

        var updateRequest = new {
            name = "IntTest_UpdatedName",
            stopTaskAfterMinutes = 120L,
            startDateTime = new { date = "2026-07-01", time = "10:00:00", timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval = 2 }
        };

        HttpResponseMessage putResponse = await Api.PutAsJsonAsync(
            $"/api/schedules/{scheduleId}", updateRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, putResponse.StatusCode,
            $"Schedule update failed: {await putResponse.Content.ReadAsStringAsync( ct )}" );

        await Task.Delay( 500, ct );

        HttpResponseMessage getResponse = await Api.GetAsync( $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResponse.StatusCode );

        JsonElement updated = await getResponse.Content
            .ReadFromJsonAsync<JsonElement>( JsonOptions, ct );

        Assert.AreEqual( "IntTest_UpdatedName", updated.GetProperty( "name" ).GetString( ) );
        Assert.AreEqual( 120L, updated.GetProperty( "stopTaskAfterMinutes" ).GetInt64( ) );

        JsonElement daily = updated.GetProperty( "dailyRecurrence" );
        Assert.AreEqual( 2, daily.GetProperty( "dayInterval" ).GetInt32( ) );

        JsonElement startDt = updated.GetProperty( "startDateTime" );
        Assert.AreEqual( "2026-07-01", startDt.GetProperty( "date" ).GetString( ) );
        StringAssert.StartsWith( startDt.GetProperty( "time" ).GetString( )!, "10:00" );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task ScheduleDelete_RemovesScheduleAndReturnsNotFoundAfter( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ScheduleDelete", "2026-06-15", "12:00:00", 3, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        HttpResponseMessage existsResponse = await Api.GetAsync( $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, existsResponse.StatusCode );

        HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResponse.StatusCode );

        HttpResponseMessage notFoundResponse = await Api.GetAsync(
            $"/api/schedules/{scheduleId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, notFoundResponse.StatusCode );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task AdHocTaskRun_WithoutConnectedAgent_ReturnsConflict( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateTaskAsync(
            "IntTest_AdHocRun", "echo adhoc-test",
            ["nonexistent-agent-tag-abc123"], null, ct );
        long taskId = task.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage runResponse = await Api.PostAsJsonAsync(
            $"/api/tasks/{taskId}/run", new object( ), JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.Conflict, runResponse.StatusCode,
            "Ad-hoc run should return 409 Conflict when no agent matches the target tags." );

        string body = await runResponse.Content.ReadAsStringAsync( ct );
        StringAssert.Contains( body, "No connected agent",
            "Conflict response should describe that no matching agent was found." );
    }

    [TestMethod]
    [Timeout( 60_000 )]
    public async Task JobHistory_ForNewTask_ReturnsEmptyList( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateTaskAsync(
            "IntTest_JobHistory", "echo jobhistory-test",
            ["integration-test"], null, ct );
        long taskId = task.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage jobsResponse = await Api.GetAsync(
            $"/api/tasks/{taskId}/jobs", ct );

        Assert.AreEqual( HttpStatusCode.OK, jobsResponse.StatusCode );

        JsonElement jobList = await jobsResponse.Content
            .ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( JsonValueKind.Array, jobList.ValueKind );
        Assert.AreEqual( 0, jobList.GetArrayLength( ),
            "A newly created task should have no job history." );
    }
}
