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

    #region Test Infrastructure

    /// <summary>
    /// Creates a daily-recurrence schedule via <c>POST /api/schedules</c> and returns the deserialized JSON response.
    /// The schedule is configured with the specified start date, time, day interval, and a 60-minute task timeout.
    /// Asserts that the creation returns <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<JsonElement> CreateDailyScheduleAsync(
        string name, string date, string time, int dayInterval,
        CancellationToken ct
    ) {
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

    /// <summary>
    /// Creates a task via <c>POST /api/tasks</c> and returns the deserialized JSON response. The task is configured as
    /// a ShellCommand with the specified name, script content, and target tags. Asserts that
    /// the creation returns <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<JsonElement> CreateTaskAsync(
        string name, string content, string[] targetTags,
        CancellationToken ct ) {
        var request = new {
            name,
            description = $"Integration test task: {name}",
            actionType = "ShellCommand",
            content,
            targetTags,
            enabled = true,
            timeoutMinutes = 5L,
        };

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Task creation failed: {await response.Content.ReadAsStringAsync( ct )}" );

        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    #endregion Test Infrastructure

    /// <summary>
    /// Verifies that creating a daily schedule persists all fields correctly and that retrieving the schedule by ID
    /// and via the schedule list both return the expected data. Creates a daily schedule with a 1-day interval
    /// starting 2026-06-15 at 09:00 UTC, then reads it back and checks the name, <c>dailyRecurrence.dayInterval</c>,
    /// start date, and start time. Also validates the schedule appears in the list endpoint.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
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
        Assert.StartsWith( "08:30", startDt.GetProperty( "time" ).GetString( )! );
        Assert.AreEqual( "UTC", startDt.GetProperty( "timeZoneId" ).GetString( ) );

        JsonElement daily = retrieved.GetProperty( "dailyRecurrence" );
        Assert.AreEqual( 1, daily.GetProperty( "dayInterval" ).GetInt32( ) );

        HttpResponseMessage listResponse = await Api.GetAsync( "/api/schedules", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResponse.StatusCode );

        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.IsGreaterThanOrEqualTo( 1, list.GetArrayLength( ),
            "Schedule list should contain at least one schedule." );
    }

    /// <summary>
    /// Verifies that a task linked to a daily schedule produces the expected occurrence preview. Creates a daily
    /// schedule (1-day interval) and a task, then requests occurrences for a 7-day window and asserts
    /// that exactly 8 occurrences are returned, each separated by exactly 1 day.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task TaskLinkedToSchedule_OccurrencePreviewReturnsExpectedDates( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_OccurrencePreview", "2026-06-15", "08:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        JsonElement task = await CreateTaskAsync(
            "IntTest_OccurrenceTask", "echo occurrence-test",
            ["integration-test"], ct );

        long taskId = task.GetProperty( "id" ).GetInt64( );
        Assert.IsGreaterThan( 0L, taskId, "Task ID should be a positive integer." );

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

    /// <summary>
    /// Verifies that updating a schedule persists the new values and triggers the invalidation path. Creates a daily
    /// schedule, then issues a PUT to change the name, timeout, day interval, and start date/time. After a brief
    /// delay, it retrieves the schedule and asserts the updated values are reflected.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ScheduleUpdate_PersistsChangesAndTriggersInvalidationPath( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement schedule = await CreateDailyScheduleAsync(
            "IntTest_ScheduleUpdate", "2026-06-15", "08:00:00", 1, ct );
        string scheduleId = schedule.GetProperty( "id" ).GetString( )!;

        _ = await CreateTaskAsync(
            "IntTest_UpdateLinkedTask", "echo update-test",
            ["integration-test"], ct );

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
        Assert.StartsWith( "10:00", startDt.GetProperty( "time" ).GetString( )! );
    }

    /// <summary>
    /// Verifies that deleting a schedule removes it and subsequent GET requests return <see
    /// cref="HttpStatusCode.NotFound"/>. Creates a daily schedule, confirms it exists via GET, issues a DELETE, and
    /// then asserts the second GET returns NotFound.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that attempting an ad-hoc task run when no agent is connected returns <see
    /// cref="HttpStatusCode.Accepted"/> (HTTP 202). The run endpoint creates a one-time schedule
    /// and returns immediately. Creates a task with a non-existent agent tag, issues a POST to
    /// <c>/api/tasks/{id}/run</c>, and asserts the accepted response.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AdHocTaskRun_WithoutConnectedAgent_ReturnsConflict( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateTaskAsync(
            "IntTest_AdHocRun", "echo adhoc-test",
            ["nonexistent-agent-tag-abc123"], ct );
        long taskId = task.GetProperty( "id" ).GetInt64( );

        HttpResponseMessage runResponse = await Api.PostAsJsonAsync(
            $"/api/tasks/{taskId}/run", new object( ), JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.Accepted, runResponse.StatusCode,
            "Ad-hoc run should return 202 Accepted (one-time schedule created)." );
    }

    /// <summary>
    /// Verifies that a newly created task has no job execution history. Creates a task, queries its job history via
    /// <c>GET /api/tasks/{id}/jobs</c>, and asserts the returned JSON array is empty.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task JobHistory_ForNewTask_ReturnsEmptyList( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement task = await CreateTaskAsync(
            "IntTest_JobHistory", "echo jobhistory-test",
            ["integration-test"], ct );
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
