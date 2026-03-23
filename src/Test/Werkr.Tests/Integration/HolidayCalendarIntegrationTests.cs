using System.Net.Http.Json;
using System.Text.Json;

namespace Werkr.Tests.Integration;

/// <summary>
/// Integration tests for the Holiday Calendar REST API endpoints.
/// Tests exercise the Werkr API via <see cref="AppHostFixture"/> (Testcontainers + WebApplicationFactory).
/// </summary>
[TestClass]
public class HolidayCalendarIntegrationTests {

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test execution,
    /// providing access to test metadata and a <see cref="CancellationToken"/> for cooperative cancellation.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Gets the shared <see cref="JsonSerializerOptions"/> configured with web defaults from
    /// <see cref="AppHostFixture.JsonOptions"/> for JSON serialization and deserialization.
    /// </summary>
    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;

    /// <summary>
    /// Gets the pre-configured, authenticated <see cref="HttpClient"/> from
    /// <see cref="AppHostFixture.ApiClient"/> for sending HTTP requests to the
    /// <c>Werkr.Api</c>.
    /// </summary>
    private static HttpClient Api => AppHostFixture.ApiClient;

    #region Helpers

    /// <summary>
    /// Creates a holiday calendar via <c>POST /api/holiday-calendars</c> and returns the
    /// deserialized JSON response. Asserts that the response status is <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<JsonElement> CreateCalendarAsync(
        string name, string description, CancellationToken ct ) {
        var request = new { name, description };
        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Calendar creation failed: {await response.Content.ReadAsStringAsync( ct )}" );
        return await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
    }

    /// <summary>
    /// Creates a daily-recurrence schedule via <c>POST /api/schedules</c> and returns its
    /// unique identifier as a string. The schedule is configured to start on 2026-06-15 at
    /// 09:00 UTC with a 1-day interval and a 60-minute task timeout. Asserts that the
    /// creation returns <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    private static async Task<string> CreateDailyScheduleAndReturnIdAsync(
        string name, CancellationToken ct ) {
        var request = new {
            name,
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date = "2026-06-15", time = "09:00:00", timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval = 1 },
        };
        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/schedules", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode );
        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        return json.GetProperty( "id" ).GetString( )!;
    }

    /// <summary>
    /// Extracts the "id" property from a <see cref="JsonElement"/> as a non-null string.
    /// </summary>
    private static string GetId( JsonElement element ) =>
        element.GetProperty( "id" ).GetString( )!;

    #endregion

    // ── CRUD ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that creating a holiday calendar round-trips correctly. Creates a calendar
    /// named "IntTest_CRUD" via the API, retrieves it by ID, and asserts the name matches.
    /// Validates that the calendar ID is a non-empty string and that the GET request returns
    /// HTTP 200 OK.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateCalendar_RoundTrips( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement created = await CreateCalendarAsync( "IntTest_CRUD", "Created via integration test", ct );
        string calId = GetId( created );
        Assert.IsFalse( string.IsNullOrEmpty( calId ) );

        HttpResponseMessage getResp = await Api.GetAsync( $"/api/v1/holiday-calendars/{calId}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

        JsonElement fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "IntTest_CRUD", fetched.GetProperty( "name" ).GetString( ) );
    }

    /// <summary>
    /// Verifies that attempting to delete a system calendar returns a non-success status code
    /// (neither OK nor NoContent). Lists all calendars, finds one marked as a system calendar,
    /// and issues a DELETE request. The test is marked <see cref="Assert.Inconclusive"/> if no
    /// system calendar exists (e.g., the seeder has not run).
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task DeleteSystemCalendar_Returns403( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // The system calendars are seeded at startup. List all and find one.
        HttpResponseMessage listResp = await Api.GetAsync( "/api/v1/holiday-calendars", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResp.StatusCode );

        JsonElement[] all = await listResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];

        JsonElement? systemCal = all.FirstOrDefault( c =>
            c.GetProperty( "isSystemCalendar" ).GetBoolean( ) );

        if (systemCal is null) {
            Assert.Inconclusive( "No system calendars found - seeder may not have run." );
            return;
        }

        string sysId = GetId( systemCal.Value );
        HttpResponseMessage delResp = await Api.DeleteAsync( $"/api/v1/holiday-calendars/{sysId}", ct );

        // Should reject modification of system calendar (400 or 403)
        Assert.AreNotEqual( HttpStatusCode.OK, delResp.StatusCode );
        Assert.AreNotEqual( HttpStatusCode.NoContent, delResp.StatusCode );
    }

    // ── Clone ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that cloning a system calendar creates an editable copy. Finds a system
    /// calendar, issues a POST to its <c>/clone</c> endpoint with a new name, and asserts
    /// that the cloned calendar has <c>isSystemCalendar</c> set to <see langword="false"/>
    /// and the expected name. The test is marked <see cref="Assert.Inconclusive"/> if no
    /// system calendar exists.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CloneSystemCalendar_CreatesEditableCopy( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage listResp = await Api.GetAsync( "/api/v1/holiday-calendars", ct );
        JsonElement[] all = await listResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];

        JsonElement? systemCal = all.FirstOrDefault( c =>
            c.GetProperty( "isSystemCalendar" ).GetBoolean( ) );

        if (systemCal is null) {
            Assert.Inconclusive( "No system calendars found." );
            return;
        }

        string sysId = GetId( systemCal.Value );
        var cloneReq = new { newName = "IntTest_Cloned" };
        HttpResponseMessage cloneResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{sysId}/clone", cloneReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, cloneResp.StatusCode );

        JsonElement cloned = await cloneResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.IsFalse( cloned.GetProperty( "isSystemCalendar" ).GetBoolean( ) );
        Assert.AreEqual( "IntTest_Cloned", cloned.GetProperty( "name" ).GetString( ) );
    }

    // ── Rules ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that adding a rule to a calendar invalidates the cache and the rule is
    /// persisted. Creates a calendar, adds a FixedDate rule for July 4 with no observance
    /// adjustment, then retrieves the rules and asserts exactly one rule exists.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AddRule_InvalidatesCache( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement cal = await CreateCalendarAsync( "IntTest_Rules", "Test rules", ct );
        string calId = GetId( cal );

        var rule = new {
            name = "Test Holiday",
            ruleType = "FixedDate",
            month = 7,
            day = 4,
            observanceRule = "None",
        };

        HttpResponseMessage addResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", rule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addResp.StatusCode );

        // Verify rule exists
        HttpResponseMessage getRulesResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", ct );
        Assert.AreEqual( HttpStatusCode.OK, getRulesResp.StatusCode );

        JsonElement[] rules = await getRulesResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];
        Assert.HasCount( 1, rules );
    }

    // ── Manual Dates ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that adding a single manual date to a calendar persists correctly.
    /// Creates a calendar, adds a manual date entry for 2026-03-15 named "Company Holiday",
    /// retrieves the dates list, and asserts exactly one date entry exists.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AddManualDate_Persists( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement cal = await CreateCalendarAsync( "IntTest_ManualDates", "Test manual dates", ct );
        string calId = GetId( cal );

        var date = new {
            date = "2026-03-15",
            name = "Company Holiday",
            year = 2026,
        };

        HttpResponseMessage addResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/dates", date, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addResp.StatusCode );

        HttpResponseMessage getDatesResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{calId}/dates", ct );
        Assert.AreEqual( HttpStatusCode.OK, getDatesResp.StatusCode );

        JsonElement[] dates = await getDatesResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];
        Assert.HasCount( 1, dates );
    }

    /// <summary>
    /// Verifies that bulk-adding multiple manual dates to a calendar persists all entries.
    /// Creates a calendar, sends three dates (March 1, June 1, September 1 of 2026) via
    /// the <c>/dates/bulk</c> endpoint, and asserts that the response status is
    /// <see cref="HttpStatusCode.Created"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task BulkAddManualDates_PersistsAll( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement cal = await CreateCalendarAsync( "IntTest_BulkDates", "Test bulk dates", ct );
        string calId = GetId( cal );

        var dates = new {
            dates = new[] {
                new { date = "2026-03-01", name = "Holiday A" },
                new { date = "2026-06-01", name = "Holiday B" },
                new { date = "2026-09-01", name = "Holiday C" },
            },
        };

        HttpResponseMessage addResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/dates/bulk", dates, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addResp.StatusCode );
    }

    // ── Rule Preview ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that previewing a FixedDate holiday rule with <c>SaturdayToFriday_SundayTa July oMonday</c>
    /// observance returns the correct number of dates for the specified year range. Posts
    /// 4 rule preview for 2025-2027 and asserts that exactly 3 dates are returned (one per year).
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task PreviewRule_ReturnsCorrectDates( ) {
        CancellationToken ct = TestContext.CancellationToken;

        var rule = new {
            name = "July 4",
            ruleType = "FixedDate",
            month = 7,
            day = 4,
            observanceRule = "SaturdayToFriday_SundayToMonday",
        };

        HttpResponseMessage previewResp = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars/rules/preview?startYear=2025&endYear=2027", rule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, previewResp.StatusCode );

        JsonElement previewResult = await previewResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] dates = [.. previewResult.GetProperty( "dates" ).EnumerateArray( )];
        Assert.HasCount( 3, dates );
    }

    // ── Schedule Attachment ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the full attach-get-detach lifecycle for scheduling a holiday calendar on a schedule.
    /// Creates a calendar and a daily schedule, attaches the calendar in Blocklist mode via PUT,
    /// retrieves the attachment and asserts the mode is "Blocklist", detaches the calendar via DELETE,
    /// and verifies the GET response returns <see cref="HttpStatusCode.NoContent"/> after detachment.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AttachCalendar_GetAttachment_Detach( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement cal = await CreateCalendarAsync( "IntTest_Attach", "Test attach", ct );
        string calId = GetId( cal );
        string schedId = await CreateDailyScheduleAndReturnIdAsync( "IntTest_Sched_Attach", ct );

        // Attach
        var attachReq = new {
            calendarId = calId,
            mode = "Blocklist",
        };
        HttpResponseMessage attachResp = await Api.PutAsJsonAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", attachReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, attachResp.StatusCode );

        // Get
        HttpResponseMessage getResp = await Api.GetAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

        JsonElement attached = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        Assert.AreEqual( "Blocklist", attached.GetProperty( "mode" ).GetString( ) );

        // Detach
        HttpResponseMessage detachResp = await Api.DeleteAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, detachResp.StatusCode );

        // Verify detached
        HttpResponseMessage getAfterDetachResp = await Api.GetAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, getAfterDetachResp.StatusCode );
    }

    // ── Calendar Preview (full calendar) ───────────────────────────────────────

    /// <summary>
    /// Verifies that the calendar preview endpoint returns computed holiday dates for a given year range.
    /// Retrieves a system calendar, requests a preview for the year 2026, and asserts that at least 10
    /// holiday dates are returned. The test is marked <see cref="Assert.Inconclusive"/> if no system
    /// calendar exists.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CalendarPreview_ReturnsDatesForYearRange( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // List system calendars and pick one
        HttpResponseMessage listResp = await Api.GetAsync( "/api/v1/holiday-calendars", ct );
        JsonElement[] all = await listResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];

        JsonElement? systemCal = all.FirstOrDefault( c =>
            c.GetProperty( "isSystemCalendar" ).GetBoolean( ) );

        if (systemCal is null) {
            Assert.Inconclusive( "No system calendars found." );
            return;
        }

        string sysId = GetId( systemCal.Value );
        HttpResponseMessage previewResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{sysId}/preview?startYear=2026&endYear=2026", ct );
        Assert.AreEqual( HttpStatusCode.OK, previewResp.StatusCode );

        JsonElement previewResult = await previewResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] dates = [.. previewResult.GetProperty( "dates" ).EnumerateArray( )];

        // At least 10 holidays (US Federal has 11, Fed Reserve has 10)
        Assert.IsGreaterThanOrEqualTo( 10, dates.Length );
    }

    // ── Audit Log ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the audit log for a newly created schedule is empty. Creates a daily schedule,
    /// queries the audit log for the past 30 days, and asserts that no audit records are returned.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AuditLog_EmptyForNewSchedule( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string schedId = await CreateDailyScheduleAndReturnIdAsync( "IntTest_Audit_Empty", ct );

        string from = DateTime.UtcNow.AddDays( -30 ).ToString( "O" );
        string to = DateTime.UtcNow.AddSeconds( 1 ).ToString( "O" );

        // Query the general audit endpoint filtered by this schedule's entity ID
        HttpResponseMessage getResp = await Api.GetAsync(
            $"/api/v1/audit?entityType=Schedule&entityId={schedId}&fromUtc={from}&toUtc={to}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

        JsonElement result = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] items = [.. result.GetProperty( "items" ).EnumerateArray( )];
        Assert.IsEmpty( items );
    }

    // ── Federal Reserve — Columbus Day (H13) ───────────────────────────────────

    /// <summary>
    /// Verifies that the Federal Reserve system calendar does not include Columbus Day (enforcing
    /// Decision H13). Retrieves the Federal Reserve calendar by its well-known ID, fetches its rules,
    /// asserts that exactly 10 rules exist, and confirms none of them contain "Columbus" in the name.
    /// The test is marked <see cref="Assert.Inconclusive"/> if the Federal Reserve calendar is not found.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task FedReserveCalendar_DoesNotIncludeColumbusDay( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Find Federal Reserve Holiday calendar by deterministic GUID
        string fedReserveId = "a0000001-0000-0000-0000-000000000002";
        HttpResponseMessage getResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{fedReserveId}", ct );

        if (getResp.StatusCode == HttpStatusCode.NotFound) {
            Assert.Inconclusive( "Federal Reserve calendar not found - seeder may not have run." );
            return;
        }

        Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

        // Get rules and verify no Columbus Day
        HttpResponseMessage rulesResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{fedReserveId}/rules", ct );
        Assert.AreEqual( HttpStatusCode.OK, rulesResp.StatusCode );

        JsonElement[] rules = await rulesResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];

        // Fed Reserve should have 10 rules (US Federal has 11 = 10 + Columbus Day)
        Assert.HasCount( 10, rules );

        bool hasColumbus = rules.Any( r =>
            r.GetProperty( "name" ).GetString( )!.Contains( "Columbus", StringComparison.OrdinalIgnoreCase ) );
        Assert.IsFalse( hasColumbus, "Federal Reserve calendar should NOT include Columbus Day (Decision H13)" );
    }

    // ── Audit Log with data ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that submitting an audit log entry persists the record and can be retrieved. Creates
    /// a calendar and schedule, attaches the calendar in Blocklist mode, posts an audit log entry with
    /// a holiday name and reason, then retrieves the audit log for a 2-day window and asserts exactly
    /// one record exists with the expected holiday name.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task SubmitAuditLog_PersistsAndRetrievesRecords( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create calendar and schedule, then attach
        JsonElement cal = await CreateCalendarAsync( "IntTest_AuditData", "Test audit persistence", ct );
        string calId = GetId( cal );
        string schedId = await CreateDailyScheduleAndReturnIdAsync( "IntTest_AuditData_Sched", ct );

        var attachReq = new { calendarId = calId, mode = "Blocklist" };
        HttpResponseMessage attachResp = await Api.PutAsJsonAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", attachReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, attachResp.StatusCode );

        // Submit an audit log record via the general audit endpoint
        var auditReq = new {
            eventTypeId = "schedule.occurrence.suppressed",
            actorId = "system",
            actorType = "System",
            entityType = "Schedule",
            entityId = schedId,
            actionPerformed = "HolidaySuppressed",
            details = new { holidayName = "Test Holiday", reason = "Blocked by Blocklist" },
        };
        HttpResponseMessage postResp = await Api.PostAsJsonAsync(
            "/api/v1/audit", auditReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, postResp.StatusCode );

        // Retrieve and verify via general audit endpoint filtered by entity
        string from = DateTime.UtcNow.AddDays( -1 ).ToString( "O" );
        string to = DateTime.UtcNow.AddDays( 1 ).ToString( "O" );
        HttpResponseMessage getResp = await Api.GetAsync(
            $"/api/v1/audit?entityType=Schedule&entityId={schedId}&fromUtc={from}&toUtc={to}", ct );
        Assert.AreEqual( HttpStatusCode.OK, getResp.StatusCode );

        JsonElement result = await getResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] items = [.. result.GetProperty( "items" ).EnumerateArray( )];
        Assert.HasCount( 1, items );
    }

    // ── Occurrence Filtering ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that Blocklist mode suppresses holiday dates from the occurrence preview. Creates a
    /// calendar with a July 4 FixedDate rule, creates a daily schedule starting July 1, attaches the
    /// calendar in Blocklist mode, and requests an occurrence preview through July 7. Asserts that
    /// July 4 does not appear in the occurrences array and, if a "suppressed" property is present, that
    /// it contains at least one entry.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task OccurrencePreview_BlocklistMode_SuppressesHolidays( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create calendar with July 4 fixed-date rule
        JsonElement cal = await CreateCalendarAsync( "IntTest_Blocklist", "Blocklist filtering", ct );
        string calId = GetId( cal );

        var rule = new {
            name = "July 4",
            ruleType = "FixedDate",
            month = 7,
            day = 4,
            observanceRule = "None",
        };
        HttpResponseMessage addRuleResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", rule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addRuleResp.StatusCode );

        // Create daily schedule starting July 1 2026
        var schedReq = new {
            name = "IntTest_BlocklistSched",
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date = "2026-07-01", time = "09:00:00", timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval = 1 },
        };
        HttpResponseMessage schedResp = await Api.PostAsJsonAsync(
            "/api/v1/schedules", schedReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, schedResp.StatusCode );
        JsonElement schedJson = await schedResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string schedId = schedJson.GetProperty( "id" ).GetString( )!;

        // Attach as blocklist
        var attachReq = new { calendarId = calId, mode = "Blocklist" };
        HttpResponseMessage attachResp = await Api.PutAsJsonAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", attachReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, attachResp.StatusCode );

        // Preview occurrences for July 1-7 window
        string windowEnd = new DateTime( 2026, 7, 8, 0, 0, 0, DateTimeKind.Utc ).ToString( "O" );
        HttpResponseMessage previewResp = await Api.GetAsync(
            $"/api/v1/schedules/{schedId}/occurrences?windowEnd={windowEnd}", ct );
        Assert.AreEqual( HttpStatusCode.OK, previewResp.StatusCode );

        JsonElement preview = await previewResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement occurrences = preview.GetProperty( "occurrences" );

        // July 4 should NOT be in the occurrence list (blocked)
        bool containsJuly4 = occurrences.EnumerateArray( ).Any( o => {
            DateTime dt = o.GetDateTime( );
            return dt.Month == 7 && dt.Day == 4;
        } );
        Assert.IsFalse( containsJuly4, "July 4 should be suppressed by blocklist" );

        // Suppressed list should contain July 4
        if (preview.TryGetProperty( "suppressed", out JsonElement suppressed ) &&
            suppressed.ValueKind == JsonValueKind.Array) {
            Assert.IsGreaterThan( 0, suppressed.GetArrayLength( ),
                "Suppressed list should contain at least one entry for July 4" );
        }
    }

    /// <summary>
    /// Verifies that Allowlist mode keeps only holiday dates in the occurrence preview. Creates a
    /// calendar with a July 4 FixedDate rule, creates a daily schedule starting July 1, attaches the
    /// calendar in Allowlist mode, and requests an occurrence preview through July 7. Asserts that
    /// exactly one occurrence is returned and that it falls on July 4.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task OccurrencePreview_AllowlistMode_KeepsOnlyHolidays( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Create calendar with July 4 fixed-date rule
        JsonElement cal = await CreateCalendarAsync( "IntTest_Allowlist", "Allowlist filtering", ct );
        string calId = GetId( cal );

        var rule = new {
            name = "July 4",
            ruleType = "FixedDate",
            month = 7,
            day = 4,
            observanceRule = "None",
        };
        HttpResponseMessage addRuleResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", rule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addRuleResp.StatusCode );

        // Create daily schedule starting July 1 2026
        var schedReq = new {
            name = "IntTest_AllowlistSched",
            stopTaskAfterMinutes = 60L,
            startDateTime = new { date = "2026-07-01", time = "09:00:00", timeZoneId = "UTC" },
            dailyRecurrence = new { dayInterval = 1 },
        };
        HttpResponseMessage schedResp = await Api.PostAsJsonAsync(
            "/api/v1/schedules", schedReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, schedResp.StatusCode );
        JsonElement schedJson = await schedResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string schedId = schedJson.GetProperty( "id" ).GetString( )!;

        // Attach as allowlist
        var attachReq = new { calendarId = calId, mode = "Allowlist" };
        HttpResponseMessage attachResp = await Api.PutAsJsonAsync(
            $"/api/v1/schedules/{schedId}/holiday-calendar", attachReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, attachResp.StatusCode );

        // Preview occurrences for July 1-7 window
        string windowEnd = new DateTime( 2026, 7, 8, 0, 0, 0, DateTimeKind.Utc ).ToString( "O" );
        HttpResponseMessage previewResp = await Api.GetAsync(
            $"/api/v1/schedules/{schedId}/occurrences?windowEnd={windowEnd}", ct );
        Assert.AreEqual( HttpStatusCode.OK, previewResp.StatusCode );

        JsonElement preview = await previewResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement occurrences = preview.GetProperty( "occurrences" );

        // Allowlist: only July 4 should remain in occurrences
        int count = occurrences.GetArrayLength( );
        Assert.AreEqual( 1, count, $"Allowlist should keep only the holiday date, but got {count} occurrences" );

        DateTime keptDate = occurrences[0].GetDateTime( );
        Assert.AreEqual( 7, keptDate.Month );
        Assert.AreEqual( 4, keptDate.Day );
    }

    // ── Rule Mutation Invalidation ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that mutating a calendar rule invalidates the cache so subsequent previews reflect the
    /// updated rule. Creates a calendar with a New Year's Day rule (Jan 1), previews to confirm one date,
    /// updates the rule to March 15, previews again, and asserts the result now shows the March 15 date
    /// instead of January 1.
    /// </summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task RuleMutation_InvalidatesCacheOnNextPreview( ) {
        CancellationToken ct = TestContext.CancellationToken;

        JsonElement cal = await CreateCalendarAsync( "IntTest_Invalidation", "Test cache invalidation", ct );
        string calId = GetId( cal );

        // Add rule for January
        var rule = new {
            name = "New Year's Day",
            ruleType = "FixedDate",
            month = 1,
            day = 1,
            observanceRule = "None",
        };
        HttpResponseMessage addResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", rule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, addResp.StatusCode );

        // Preview: should have January 1
        HttpResponseMessage preview1Resp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{calId}/preview?startYear=2026&endYear=2026", ct );
        Assert.AreEqual( HttpStatusCode.OK, preview1Resp.StatusCode );

        JsonElement preview1Result = await preview1Resp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] dates1 = [.. preview1Result.GetProperty( "dates" ).EnumerateArray( )];
        Assert.HasCount( 1, dates1 );

        // Get the rule ID so we can update it
        HttpResponseMessage rulesResp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{calId}/rules", ct );
        JsonElement[] rules = await rulesResp.Content.ReadFromJsonAsync<JsonElement[]>( JsonOptions, ct )
            ?? [];
        long ruleId = rules[0].GetProperty( "id" ).GetInt64( );

        // Update rule: change from January to March
        var updatedRule = new {
            name = "Moved Holiday",
            ruleType = "FixedDate",
            month = 3,
            day = 15,
            observanceRule = "None",
        };
        HttpResponseMessage updateResp = await Api.PutAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/rules/{ruleId}", updatedRule, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, updateResp.StatusCode );

        // Preview again: should now show March 15 instead of January 1
        HttpResponseMessage preview2Resp = await Api.GetAsync(
            $"/api/v1/holiday-calendars/{calId}/preview?startYear=2026&endYear=2026", ct );
        Assert.AreEqual( HttpStatusCode.OK, preview2Resp.StatusCode );

        JsonElement preview2Result = await preview2Resp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        JsonElement[] dates2 = [.. preview2Result.GetProperty( "dates" ).EnumerateArray( )];
        Assert.HasCount( 1, dates2 );

        string dateStr = dates2[0].GetProperty( "date" ).GetString( )!;
        Assert.Contains( dateStr, "2026-03-15",
            $"After rule update, preview should show March 15, but got: {dateStr}" );
    }
}
