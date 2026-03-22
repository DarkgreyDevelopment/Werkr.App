using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;
using Werkr.Core.Retention;

namespace Werkr.Tests.Integration;

[TestClass]
public class RetentionIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    /// <summary>GET /api/v1/retention/policies returns all seeded policies.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetPolicies_ReturnsSeededPolicies( ) {
        CancellationToken ct = TestContext.CancellationToken;

        List<RetentionPolicyDto>? policies =
            await Api.GetFromJsonAsync<List<RetentionPolicyDto>>( "/api/v1/retention/policies", JsonOptions, ct );

        Assert.IsNotNull( policies );
        Assert.IsGreaterThan( 0, policies.Count );
        Assert.IsTrue( policies.Exists( p => p.EntityType == "workflow_run" ) );
        Assert.IsTrue( policies.Exists( p => p.EntityType == "audit_log" ) );
        Assert.IsTrue( policies.Exists( p => p.EntityType == "job_output" ) );
        Assert.IsTrue( policies.Exists( p => p.EntityType == "variable_version" ) );
    }

    /// <summary>PUT /api/v1/retention/policies/{type} updates retention days.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdatePolicy_ChangesRetentionDays( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RetentionPolicyUpdateRequest request = new( 90 );
        HttpResponseMessage response = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/workflow_run", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        RetentionPolicyDto? updated = await response.Content
            .ReadFromJsonAsync<RetentionPolicyDto>( JsonOptions, ct );
        Assert.IsNotNull( updated );
        Assert.AreEqual( 90, updated.RetentionDays );

        // Restore default
        RetentionPolicyUpdateRequest restore = new( 180 );
        _ = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/workflow_run", restore, JsonOptions, ct );
    }

    /// <summary>PUT /api/v1/retention/policies/{type} accepts 0 days (immediate deletion).</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdatePolicy_AcceptsZeroDays( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RetentionPolicyUpdateRequest request = new( 0 );
        HttpResponseMessage response = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/job_output", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        RetentionPolicyDto? updated = await response.Content
            .ReadFromJsonAsync<RetentionPolicyDto>( JsonOptions, ct );
        Assert.IsNotNull( updated );
        Assert.AreEqual( 0, updated.RetentionDays );

        // Restore default
        RetentionPolicyUpdateRequest restore = new( 180 );
        _ = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/job_output", restore, JsonOptions, ct );
    }

    /// <summary>PUT /api/v1/retention/policies/{type} rejects negative days.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdatePolicy_RejectsNegativeDays( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RetentionPolicyUpdateRequest request = new( -1 );
        HttpResponseMessage response = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/workflow_run", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
    }

    /// <summary>POST /api/v1/retention/sweep?dryRun=true returns preview without deleting.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task DryRunSweep_ReturnsPreview( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.PostAsync(
            "/api/v1/retention/sweep?dryRun=true", null, ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        List<RetentionSweepResult>? results = await response.Content
            .ReadFromJsonAsync<List<RetentionSweepResult>>( JsonOptions, ct );
        Assert.IsNotNull( results );
        // Should have results for each enabled policy with a registered provider
        Assert.IsGreaterThan( 0, results.Count );
    }

    /// <summary>POST /api/v1/retention/sweep executes sweep and returns results.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ExecuteSweep_ReturnsResults( ) {
        CancellationToken ct = TestContext.CancellationToken;

        HttpResponseMessage response = await Api.PostAsync(
            "/api/v1/retention/sweep", null, ct );

        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );

        List<RetentionSweepResult>? results = await response.Content
            .ReadFromJsonAsync<List<RetentionSweepResult>>( JsonOptions, ct );
        Assert.IsNotNull( results );
    }

    /// <summary>PUT /api/v1/retention/policies/{type} returns 404 for unknown entity type.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdatePolicy_ReturnsNotFoundForUnknownType( ) {
        CancellationToken ct = TestContext.CancellationToken;

        RetentionPolicyUpdateRequest request = new( 30 );
        HttpResponseMessage response = await Api.PutAsJsonAsync(
            "/api/v1/retention/policies/nonexistent_type", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.NotFound, response.StatusCode );
    }
}
