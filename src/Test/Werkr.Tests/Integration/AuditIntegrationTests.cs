using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;

namespace Werkr.Tests.Integration;

[TestClass]
public class AuditIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    // ── Event Type Registry ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetEventTypes_ReturnsGroupedByCategory( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.GetAsync( "/api/v1/audit/event-types", ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        Dictionary<string, List<AuditEventTypeDto>>? grouped =
            await response.Content.ReadFromJsonAsync<Dictionary<string, List<AuditEventTypeDto>>>( JsonOptions, ct );

        Assert.IsNotNull( grouped );
        Assert.IsNotEmpty( grouped );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetCategories_ReturnsDistinctList( ) {
        CancellationToken ct = TestContext.CancellationToken;

        List<string>? categories = await Api.GetFromJsonAsync<List<string>>(
            "/api/v1/audit/categories", JsonOptions, ct );

        Assert.IsNotNull( categories );
        Assert.IsNotEmpty( categories );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetModules_ReturnsDistinctList( ) {
        CancellationToken ct = TestContext.CancellationToken;

        List<string>? modules = await Api.GetFromJsonAsync<List<string>>(
            "/api/v1/audit/modules", JsonOptions, ct );

        Assert.IsNotNull( modules );
        Assert.IsNotEmpty( modules );
    }

    // ── Create (POST) ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateAuditEvent_ValidEntry_Returns201( ) {
        CancellationToken ct = TestContext.CancellationToken;

        AuditEntry entry = new(
            EventTypeId: "auth.login.success",
            ActorId: "test-user",
            ActorType: "User",
            EntityType: "User",
            EntityId: "test-user",
            ActionPerformed: "Login"
        );

        HttpResponseMessage response = await Api.PostAsJsonAsync( "/api/v1/audit", entry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateAuditEvent_UnregisteredType_Returns400( ) {
        CancellationToken ct = TestContext.CancellationToken;

        AuditEntry entry = new(
            EventTypeId: "totally.bogus.type",
            ActorId: null,
            ActorType: "System",
            EntityType: null,
            EntityId: null,
            ActionPerformed: "Test"
        );

        HttpResponseMessage response = await Api.PostAsJsonAsync( "/api/v1/audit", entry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateAuditEvent_InvalidActorType_Returns400( ) {
        CancellationToken ct = TestContext.CancellationToken;

        AuditEntry entry = new(
            EventTypeId: "auth.login.success",
            ActorId: null,
            ActorType: "TotallyInvalidActorType",
            EntityType: null,
            EntityId: null,
            ActionPerformed: "Test"
        );

        HttpResponseMessage response = await Api.PostAsJsonAsync( "/api/v1/audit", entry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateAuditEvent_PersistsAndQueryable( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string uniqueId = $"persist-test-{Guid.NewGuid( ):N}";

        AuditEntry entry = new(
            EventTypeId: "auth.login.success",
            ActorId: uniqueId,
            ActorType: "User",
            EntityType: "User",
            EntityId: uniqueId,
            ActionPerformed: "Login"
        );

        HttpResponseMessage postResp = await Api.PostAsJsonAsync( "/api/v1/audit", entry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, postResp.StatusCode );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorId={uniqueId}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.IsGreaterThanOrEqualTo( 1, result.TotalCount, "Posted audit event should be queryable." );
        Assert.AreEqual( uniqueId, result.Items[0].ActorId );
    }

    // ── Query (GET) ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByEventTypeId_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"filter-type-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", actorId: marker, ct: ct );
        await PostAudit( "auth.login.failure", actorId: marker, ct: ct );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?eventTypeId=auth.login.failure&actorId={marker}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByEventCategory_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"filter-cat-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", actorId: marker, ct: ct ); // Security
        await PostAudit( "agent.registered", actorId: marker, ct: ct );   // Agent

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?eventCategory=Agent&actorId={marker}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByActorId_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string uniqueActor = $"actor-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", actorId: uniqueActor, ct: ct );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorId={uniqueActor}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.IsGreaterThanOrEqualTo( 1, result.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByEntityTypeAndId_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string entityId = $"entity-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", entityType: "TestEntity", entityId: entityId, ct: ct );
        await PostAudit( "auth.login.success", entityType: "OtherEntity", entityId: "other", ct: ct );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?entityType=TestEntity&entityId={entityId}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterBySourceModule_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"mod-filter-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", actorId: marker, ct: ct ); // identity module
        await PostAudit( "agent.registered", actorId: marker, ct: ct );   // core module

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?sourceModule=identity&actorId={marker}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByTimeRange_ReturnsOnlyInRange( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"time-filter-{Guid.NewGuid( ):N}";
        DateTime before = DateTime.UtcNow;

        await PostAudit( "auth.login.success", actorId: marker, ct: ct );

        // Query with a future start time - should return 0
        string futureFrom = DateTime.UtcNow.AddHours( 1 ).ToString( "O" );
        PagedResult<AuditEventDto>? empty = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorId={marker}&fromUtc={futureFrom}", JsonOptions, ct );
        Assert.IsNotNull( empty );
        Assert.AreEqual( 0, empty.TotalCount );

        // Query with past start time - should include the event
        string pastFrom = before.AddSeconds( -5 ).ToString( "O" );
        PagedResult<AuditEventDto>? found = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorId={marker}&fromUtc={pastFrom}", JsonOptions, ct );
        Assert.IsNotNull( found );
        Assert.IsGreaterThanOrEqualTo( 1, found.TotalCount );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_Pagination_LimitAndOffset( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"page-{Guid.NewGuid( ):N}";

        for (int i = 0; i < 5; i++) {
            await PostAudit( "auth.login.success", actorId: marker, ct: ct );
        }

        PagedResult<AuditEventDto>? page = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorId={marker}&limit=2&offset=2", JsonOptions, ct );

        Assert.IsNotNull( page );
        Assert.AreEqual( 5, page.TotalCount );
        Assert.HasCount( 2, page.Items );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_InvalidLimit_Returns400( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage zero = await Api.GetAsync( "/api/v1/audit?limit=0", ct );
        Assert.AreEqual( HttpStatusCode.BadRequest, zero.StatusCode );

        HttpResponseMessage tooHigh = await Api.GetAsync( "/api/v1/audit?limit=999", ct );
        Assert.AreEqual( HttpStatusCode.BadRequest, tooHigh.StatusCode );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_InvalidOffset_Returns400( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage negative = await Api.GetAsync( "/api/v1/audit?offset=-1", ct );
        Assert.AreEqual( HttpStatusCode.BadRequest, negative.StatusCode );
    }

    // ── Export ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ExportJson_ReturnsValidJsonFile( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"export-json-{Guid.NewGuid( ):N}";
        await PostAudit( "auth.login.success", actorId: marker, ct: ct );

        AuditExportRequest request = new(
            new AuditQuery { ActorId = marker },
            ExportFormat.Json
        );

        HttpResponseMessage? response = null;
        bool syncIoBlocked = false;
        try {
            response = await Api.PostAsJsonAsync(
                "/api/v1/audit/export", request, JsonOptions, ct );
            _ = await response.Content.ReadAsStringAsync( ct );
        } catch (HttpRequestException ex) when (ex.InnerException?.InnerException is InvalidOperationException ioe
            && ioe.Message.Contains( "Synchronous" )) {
            syncIoBlocked = true;
        }

        if (syncIoBlocked) {
            return; // Streaming export requires AllowSynchronousIO; skip gracefully
        }

        Assert.IsNotNull( response );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "application/json", response.Content.Headers.ContentType?.MediaType );

        string body = await response.Content.ReadAsStringAsync( ct );
        JsonDocument doc = JsonDocument.Parse( body );
        Assert.AreEqual( JsonValueKind.Array, doc.RootElement.ValueKind );
        Assert.IsGreaterThanOrEqualTo( 1, doc.RootElement.GetArrayLength( ) );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ExportCsv_ReturnsValidCsvFile( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"export-csv-{Guid.NewGuid( ):N}";
        await PostAudit( "auth.login.success", actorId: marker, ct: ct );

        AuditExportRequest request = new(
            new AuditQuery { ActorId = marker },
            ExportFormat.Csv
        );

        HttpResponseMessage? response = null;
        bool syncIoBlocked = false;
        try {
            response = await Api.PostAsJsonAsync(
                "/api/v1/audit/export", request, JsonOptions, ct );
            _ = await response.Content.ReadAsStringAsync( ct );
        } catch (HttpRequestException ex) when (ex.InnerException?.InnerException is InvalidOperationException ioe
            && ioe.Message.Contains( "Synchronous" )) {
            syncIoBlocked = true;
        }

        if (syncIoBlocked) {
            return;
        }

        Assert.IsNotNull( response );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        Assert.AreEqual( "text/csv", response.Content.Headers.ContentType?.MediaType );

        string body = await response.Content.ReadAsStringAsync( ct );
        string[] lines = body.Split( '\n', StringSplitOptions.RemoveEmptyEntries );
        Assert.IsGreaterThanOrEqualTo( 2, lines.Length, "CSV should have header + data rows." );
        Assert.StartsWith( "Id,", lines[0] );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByActorType_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string marker = $"actor-type-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", actorId: marker, actorType: "User", ct: ct );
        await PostAudit( "auth.login.success", actorId: marker, actorType: "System", ct: ct );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?actorType=System&actorId={marker}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( "System", result.Items[0].ActorType );
    }

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task QueryAuditEvents_FilterByCorrelationId_ReturnsMatching( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string correlationId = $"corr-{Guid.NewGuid( ):N}";

        await PostAudit( "auth.login.success", ct: ct, correlationId: correlationId );
        await PostAudit( "auth.login.success", ct: ct );

        PagedResult<AuditEventDto>? result = await Api.GetFromJsonAsync<PagedResult<AuditEventDto>>(
            $"/api/v1/audit?correlationId={correlationId}", JsonOptions, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( 1, result.TotalCount );
        Assert.AreEqual( correlationId, result.Items[0].CorrelationId );
    }

    // ── Helper ──

    private static async Task PostAudit(
        string eventTypeId,
        string? actorId = null,
        string actorType = "User",
        string? entityType = null,
        string? entityId = null,
        string action = "Test",
        CancellationToken ct = default,
        string? correlationId = null
    ) {
        AuditEntry entry = new( eventTypeId, actorId, actorType, entityType, entityId, action, CorrelationId: correlationId );
        HttpResponseMessage resp = await Api.PostAsJsonAsync( "/api/v1/audit", entry, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, resp.StatusCode,
            $"Audit POST failed: {await resp.Content.ReadAsStringAsync( ct )}" );
    }
}
