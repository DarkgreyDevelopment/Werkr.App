using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;

namespace Werkr.Tests.Integration;

[TestClass]
public class WorkflowVersionIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    private async Task<WorkflowDto> CreateTestWorkflowAsync( CancellationToken ct, string name = "Version Test Workflow" ) {
        WorkflowCreateRequest request = new(
            Name: name,
            Description: "Integration test workflow",
            Enabled: true,
            TargetTags: ["test"] );

        HttpResponseMessage response = await Api.PostAsJsonAsync( "/api/v1/workflows", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode );
        WorkflowDto? created = await response.Content.ReadFromJsonAsync<WorkflowDto>( JsonOptions, ct );
        Assert.IsNotNull( created );
        return created;
    }

    private async Task<WorkflowDto> UpdateTestWorkflowAsync( long workflowId, string newName, CancellationToken ct,
        string? changeDescription = null, int? expectedVersionNumber = null ) {
        WorkflowUpdateRequest request = new(
            Name: newName,
            Description: "Updated description",
            Enabled: true,
            TargetTags: ["test"],
            ChangeDescription: changeDescription,
            ExpectedVersionNumber: expectedVersionNumber );

        HttpResponseMessage response = await Api.PutAsJsonAsync( $"/api/v1/workflows/{workflowId}", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        WorkflowDto? updated = await response.Content.ReadFromJsonAsync<WorkflowDto>( JsonOptions, ct );
        Assert.IsNotNull( updated );
        return updated;
    }

    // ── CreateWorkflow creates initial version ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateWorkflow_CreatesInitialVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF V1 Initial Test" );

        Assert.IsNotNull( workflow.CurrentVersionId );
        Assert.AreEqual( 1, workflow.CurrentVersionNumber );

        PagedResult<WorkflowVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        Assert.AreEqual( 1, versions.TotalCount );
        Assert.AreEqual( 1, versions.Items[0].VersionNumber );
        Assert.AreEqual( "Initial version", versions.Items[0].ChangeDescription );
    }

    // ── UpdateWorkflow creates new version ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdateWorkflow_CreatesNewVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF V2 Update Test" );
        WorkflowDto updated = await UpdateTestWorkflowAsync( workflow.Id, "WF V2 Updated Name", ct, "Changed the name" );

        Assert.AreEqual( 2, updated.CurrentVersionNumber );

        PagedResult<WorkflowVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        Assert.AreEqual( 2, versions.TotalCount );
        // Newest first
        Assert.AreEqual( 2, versions.Items[0].VersionNumber );
        Assert.AreEqual( "Changed the name", versions.Items[0].ChangeDescription );
        Assert.AreEqual( 1, versions.Items[1].VersionNumber );
    }

    // ── Snapshot matches updated state ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdateWorkflow_SnapshotMatchesState( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF Snapshot Match Test" );
        _ = await UpdateTestWorkflowAsync( workflow.Id, "WF Snapshot Updated", ct );

        PagedResult<WorkflowVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        WorkflowVersionDto v2 = versions.Items[0];
        Assert.AreEqual( 2, v2.VersionNumber );

        // Parse the definition JSON to verify it reflects the updated state
        using JsonDocument doc = JsonDocument.Parse( v2.Definition );
        Assert.AreEqual( "WF Snapshot Updated", doc.RootElement.GetProperty( "name" ).GetString( ) );
    }

    // ── GetVersionDiff returns changes ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetVersionDiff_ReturnsChanges( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF Diff Test Original" );
        _ = await UpdateTestWorkflowAsync( workflow.Id, "WF Diff Test Changed", ct );

        PagedResult<WorkflowVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        long fromId = versions.Items.First( v => v.VersionNumber == 1 ).Id;
        long toId = versions.Items.First( v => v.VersionNumber == 2 ).Id;

        WorkflowVersionDiffResponse? diff = await Api.GetFromJsonAsync<WorkflowVersionDiffResponse>(
            $"/api/v1/workflows/{workflow.Id}/versions/diff?from={fromId}&to={toId}", JsonOptions, ct );

        Assert.IsNotNull( diff );
        Assert.AreEqual( 1, diff.FromVersionNumber );
        Assert.AreEqual( 2, diff.ToVersionNumber );
        Assert.IsNotEmpty( diff.Changes );

        // Name should be in the diff
        WorkflowVersionDiffEntry? nameChange = diff.Changes.FirstOrDefault( c => c.PropertyPath == "Name" );
        Assert.IsNotNull( nameChange );
        Assert.AreEqual( "WF Diff Test Original", nameChange.OldValue );
        Assert.AreEqual( "WF Diff Test Changed", nameChange.NewValue );
        Assert.AreEqual( DiffChangeType.Modified, nameChange.ChangeType );
    }

    // ── Pagination works correctly ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Pagination_ReturnsCorrectPage( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF Paging Test" );
        // Create 3 more versions (total: 4 including initial)
        for (int i = 2; i <= 4; i++) {
            _ = await UpdateTestWorkflowAsync( workflow.Id, $"WF Paging v{i}", ct, $"Version {i}" );
        }

        // First page of 2
        PagedResult<WorkflowVersionDto>? page1 = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions?limit=2&offset=0", JsonOptions, ct );

        Assert.IsNotNull( page1 );
        Assert.AreEqual( 4, page1.TotalCount );
        Assert.HasCount( 2, page1.Items );
        Assert.AreEqual( 4, page1.Items[0].VersionNumber ); // Newest first
        Assert.AreEqual( 3, page1.Items[1].VersionNumber );

        // Second page of 2
        PagedResult<WorkflowVersionDto>? page2 = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions?limit=2&offset=2", JsonOptions, ct );

        Assert.IsNotNull( page2 );
        Assert.AreEqual( 4, page2.TotalCount );
        Assert.HasCount( 2, page2.Items );
        Assert.AreEqual( 2, page2.Items[0].VersionNumber );
        Assert.AreEqual( 1, page2.Items[1].VersionNumber );
    }

    // ── Version belongs to correct workflow (cross-workflow isolation) ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Version_BelongsToCorrectWorkflow( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto wfA = await CreateTestWorkflowAsync( ct, "WF Isolation A" );
        WorkflowDto wfB = await CreateTestWorkflowAsync( ct, "WF Isolation B" );

        // Each should have exactly 1 version
        PagedResult<WorkflowVersionDto>? versionsA = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{wfA.Id}/versions", JsonOptions, ct );
        PagedResult<WorkflowVersionDto>? versionsB = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{wfB.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versionsA );
        Assert.IsNotNull( versionsB );
        Assert.AreEqual( 1, versionsA.TotalCount );
        Assert.AreEqual( 1, versionsB.TotalCount );

        // Cannot access wfA's version via wfB's endpoint
        long versionAId = versionsA.Items[0].Id;
        HttpResponseMessage crossResponse = await Api.GetAsync(
            $"/api/v1/workflows/{wfB.Id}/versions/{versionAId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, crossResponse.StatusCode );
    }

    // ── Rollback creates new version with old definition ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Rollback_CreatesNewVersionWithOldDefinition( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF Rollback Test Original" );
        _ = await UpdateTestWorkflowAsync( workflow.Id, "WF Rollback Test Changed", ct );

        // Should have 2 versions
        PagedResult<WorkflowVersionDto>? versionsBeforeRollback = await Api.GetFromJsonAsync<PagedResult<WorkflowVersionDto>>(
            $"/api/v1/workflows/{workflow.Id}/versions", JsonOptions, ct );
        Assert.IsNotNull( versionsBeforeRollback );
        Assert.AreEqual( 2, versionsBeforeRollback.TotalCount );
        long v1Id = versionsBeforeRollback.Items.First( v => v.VersionNumber == 1 ).Id;

        // Rollback to v1
        HttpResponseMessage rollbackResponse = await Api.PostAsync(
            $"/api/v1/workflows/{workflow.Id}/versions/{v1Id}/rollback", null, ct );
        Assert.AreEqual( HttpStatusCode.OK, rollbackResponse.StatusCode );

        WorkflowVersionDto? rollbackVersion = await rollbackResponse.Content.ReadFromJsonAsync<WorkflowVersionDto>( JsonOptions, ct );
        Assert.IsNotNull( rollbackVersion );
        Assert.AreEqual( 3, rollbackVersion.VersionNumber ); // New version created

        // Verify the definition matches v1's original definition
        using JsonDocument rollbackDoc = JsonDocument.Parse( rollbackVersion.Definition );
        Assert.AreEqual( "WF Rollback Test Original", rollbackDoc.RootElement.GetProperty( "name" ).GetString( ) );
    }

    // ── Optimistic concurrency returns 409 on mismatch ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task OptimisticConcurrency_Returns409OnMismatch( ) {
        CancellationToken ct = TestContext.CancellationToken;

        WorkflowDto workflow = await CreateTestWorkflowAsync( ct, "WF Concurrency Test" );

        // Try to update with a wrong expected version number
        WorkflowUpdateRequest request = new(
            Name: "WF Concurrency Test Updated",
            Description: "Should fail",
            ExpectedVersionNumber: 999 ); // Wrong version

        HttpResponseMessage response = await Api.PutAsJsonAsync(
            $"/api/v1/workflows/{workflow.Id}", request, JsonOptions, ct );

        Assert.AreEqual( HttpStatusCode.Conflict, response.StatusCode );
    }
}
