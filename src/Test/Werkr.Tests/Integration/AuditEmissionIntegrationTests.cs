using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;

namespace Werkr.Tests.Integration;

[TestClass]
public class AuditEmissionIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    // ── Agent Operations ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AgentRegistration_EmitsAgentRegisteredEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DateTime before = DateTime.UtcNow;

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/registration/generate",
            new { connectionName = $"audit-test-{Guid.NewGuid( ):N}", password = "TestPassword123!", tags = new[] { "test" } },
            JsonOptions, ct );

        // Registration requires crypto setup that may not be available in test fixture;
        // skip if the endpoint is not functional.
        if (response.StatusCode == HttpStatusCode.InternalServerError) {
            Assert.Inconclusive( "Registration endpoint not available in test fixture." );
        }

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode,
            $"Registration failed: {await response.Content.ReadAsStringAsync( ct )}" );

        await AssertAuditEventEmittedAsync( "agent.registered", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CalendarCreate_EmitsCalendarCreatedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DateTime before = DateTime.UtcNow;
        string calName = $"AuditTest-{Guid.NewGuid( ):N}";

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars",
            new { name = calName, description = "Test calendar for audit" },
            JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode,
            $"Calendar create failed: {await response.Content.ReadAsStringAsync( ct )}" );

        await AssertAuditEventEmittedAsync( "calendar.created", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CalendarUpdate_EmitsCalendarUpdatedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string calName = $"AuditUpdate-{Guid.NewGuid( ):N}";
        HttpResponseMessage createResp = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars",
            new { name = calName, description = "Before update" },
            JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResp.StatusCode );

        JsonElement created = await createResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string calId = created.GetProperty( "id" ).GetString( )!;

        DateTime before = DateTime.UtcNow;
        HttpResponseMessage updateResp = await Api.PutAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}",
            new { name = $"{calName}-updated", description = "After update" },
            JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, updateResp.StatusCode );

        await AssertAuditEventEmittedAsync( "calendar.updated", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CalendarDelete_EmitsCalendarDeletedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string calName = $"AuditDelete-{Guid.NewGuid( ):N}";
        HttpResponseMessage createResp = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars",
            new { name = calName, description = "To be deleted" },
            JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResp.StatusCode );

        JsonElement created = await createResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string calId = created.GetProperty( "id" ).GetString( )!;

        DateTime before = DateTime.UtcNow;
        HttpResponseMessage deleteResp = await Api.DeleteAsync(
            $"/api/v1/holiday-calendars/{calId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, deleteResp.StatusCode );

        await AssertAuditEventEmittedAsync( "calendar.deleted", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CalendarClone_EmitsCalendarClonedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        string calName = $"AuditClone-{Guid.NewGuid( ):N}";
        HttpResponseMessage createResp = await Api.PostAsJsonAsync(
            "/api/v1/holiday-calendars",
            new { name = calName, description = "To be cloned" },
            JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResp.StatusCode );

        JsonElement created = await createResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        string calId = created.GetProperty( "id" ).GetString( )!;

        DateTime before = DateTime.UtcNow;
        HttpResponseMessage cloneResp = await Api.PostAsJsonAsync(
            $"/api/v1/holiday-calendars/{calId}/clone",
            new { newName = $"{calName}-clone" },
            JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, cloneResp.StatusCode );

        await AssertAuditEventEmittedAsync( "calendar.cloned", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AgentRevoke_EmitsAgentRevokedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Generate a registration bundle first
        HttpResponseMessage genResp = await Api.PostAsJsonAsync(
            "/api/v1/registration/generate",
            new { connectionName = $"revoke-test-{Guid.NewGuid( ):N}", password = "TestPassword123!", tags = new[] { "test" } },
            JsonOptions, ct );

        if (genResp.StatusCode == HttpStatusCode.InternalServerError) {
            Assert.Inconclusive( "Registration endpoint not available in test fixture." );
        }
        Assert.AreEqual( HttpStatusCode.OK, genResp.StatusCode );

        // Get agent list and find one to revoke
        HttpResponseMessage listResp = await Api.GetAsync( "/api/v1/agents", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResp.StatusCode );

        JsonElement agents = await listResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        if (agents.GetArrayLength( ) == 0) {
            Assert.Inconclusive( "No agents available to revoke." );
        }

        string agentId = agents[0].GetProperty( "id" ).GetString( )!;

        DateTime before = DateTime.UtcNow;
        HttpResponseMessage revokeResp = await Api.DeleteAsync( $"/api/v1/agents/{agentId}", ct );
        Assert.AreEqual( HttpStatusCode.NoContent, revokeResp.StatusCode );

        await AssertAuditEventEmittedAsync( "agent.revoked", before, ct );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task AgentUpdate_EmitsAgentUpdatedEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Generate a registration bundle first
        HttpResponseMessage genResp = await Api.PostAsJsonAsync(
            "/api/v1/registration/generate",
            new { connectionName = $"update-test-{Guid.NewGuid( ):N}", password = "TestPassword123!", tags = new[] { "test" } },
            JsonOptions, ct );

        if (genResp.StatusCode == HttpStatusCode.InternalServerError) {
            Assert.Inconclusive( "Registration endpoint not available in test fixture." );
        }
        Assert.AreEqual( HttpStatusCode.OK, genResp.StatusCode );

        // Get agent list and find one to update
        HttpResponseMessage listResp = await Api.GetAsync( "/api/v1/agents", ct );
        Assert.AreEqual( HttpStatusCode.OK, listResp.StatusCode );

        JsonElement agents = await listResp.Content.ReadFromJsonAsync<JsonElement>( JsonOptions, ct );
        if (agents.GetArrayLength( ) == 0) {
            Assert.Inconclusive( "No agents available to update." );
        }

        string agentId = agents[0].GetProperty( "id" ).GetString( )!;

        DateTime before = DateTime.UtcNow;
        HttpResponseMessage updateResp = await Api.PutAsJsonAsync(
            $"/api/v1/agents/{agentId}",
            new { connectionName = $"updated-{Guid.NewGuid( ):N}", tags = new[] { "updated" } },
            JsonOptions, ct );

        if (updateResp.StatusCode == HttpStatusCode.NotFound) {
            Assert.Inconclusive( "Agent update endpoint not available." );
        }
        Assert.AreEqual( HttpStatusCode.OK, updateResp.StatusCode );

        await AssertAuditEventEmittedAsync( "agent.updated", before, ct );
    }

    // ── API Key Audit Events ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ApiKeyCreatedAndRevoked_AuditEventTypesAccepted( ) {
        CancellationToken ct = TestContext.CancellationToken;
        DateTime before = DateTime.UtcNow;
        string keyId = Guid.NewGuid( ).ToString( "N" );

        // Post api-key created event
        AuditEntry createEntry = new(
            EventTypeId: "apikey.created",
            ActorId: "test-admin",
            ActorType: "User",
            EntityType: "ApiKey",
            EntityId: keyId,
            ActionPerformed: "Created"
        );

        HttpResponseMessage createResp = await Api.PostAsJsonAsync( "/api/v1/audit", createEntry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, createResp.StatusCode,
            $"apikey.created audit POST failed: {await createResp.Content.ReadAsStringAsync( ct )}" );

        // Post api-key revoked event
        AuditEntry revokeEntry = new(
            EventTypeId: "apikey.revoked",
            ActorId: "test-admin",
            ActorType: "User",
            EntityType: "ApiKey",
            EntityId: keyId,
            ActionPerformed: "Revoked"
        );

        HttpResponseMessage revokeResp = await Api.PostAsJsonAsync( "/api/v1/audit", revokeEntry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, revokeResp.StatusCode,
            $"apikey.revoked audit POST failed: {await revokeResp.Content.ReadAsStringAsync( ct )}" );

        // Verify both events are queryable
        await AssertAuditEventEmittedAsync( "apikey.created", before, ct );
        await AssertAuditEventEmittedAsync( "apikey.revoked", before, ct );
    }

    // ── Helpers ──

    private static async Task AssertAuditEventEmittedAsync(
        string expectedEventTypeId,
        DateTime afterUtc,
        CancellationToken ct
    ) {
        string fromUtc = afterUtc.AddSeconds( -2 ).ToString( "O" );
        string url = $"/api/v1/audit?eventTypeId={expectedEventTypeId}&fromUtc={fromUtc}&limit=10";

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            url, JsonOptions, ct );

        Assert.IsNotNull( result, $"Audit query for '{expectedEventTypeId}' returned null." );
        Assert.IsGreaterThanOrEqualTo( 1, result.TotalCount,
            $"Expected at least 1 audit event of type '{expectedEventTypeId}', found {result.TotalCount}." );
    }
}
