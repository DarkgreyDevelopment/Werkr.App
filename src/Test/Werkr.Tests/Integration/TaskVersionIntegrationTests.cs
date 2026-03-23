using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;

namespace Werkr.Tests.Integration;

[TestClass]
public class TaskVersionIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    private async Task<TaskDto> CreateTestTaskAsync( CancellationToken ct, string name = "Version Test Task" ) {
        TaskCreateRequest request = new(
            Name: name,
            Description: "Integration test task",
            ActionType: "ShellCommand",
            Content: "echo hello",
            Arguments: null,
            TargetTags: ["test"] );

        HttpResponseMessage response = await Api.PostAsJsonAsync( "/api/v1/tasks", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode );
        TaskDto? created = await response.Content.ReadFromJsonAsync<TaskDto>( JsonOptions, ct );
        Assert.IsNotNull( created );
        return created;
    }

    private async Task<TaskDto> UpdateTestTaskAsync( long taskId, string newName, CancellationToken ct,
        string? changeDescription = null ) {
        TaskUpdateRequest request = new(
            Name: newName,
            Description: "Updated description",
            ActionType: "ShellCommand",
            Content: "echo updated",
            Arguments: null,
            TargetTags: ["test"],
            ChangeDescription: changeDescription );

        HttpResponseMessage response = await Api.PutAsJsonAsync( $"/api/v1/tasks/{taskId}", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
        TaskDto? updated = await response.Content.ReadFromJsonAsync<TaskDto>( JsonOptions, ct );
        Assert.IsNotNull( updated );
        return updated;
    }

    // ── CreateTask creates initial version ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task CreateTask_CreatesInitialVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "V1 Initial Test" );

        Assert.IsNotNull( task.CurrentVersionId );
        Assert.AreEqual( 1, task.CurrentVersionNumber );

        PagedResult<TaskVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        Assert.AreEqual( 1, versions.TotalCount );
        Assert.AreEqual( 1, versions.Items[0].VersionNumber );
        Assert.AreEqual( "Initial version", versions.Items[0].ChangeDescription );
    }

    // ── UpdateTask creates new version ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task UpdateTask_CreatesNewVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "V2 Update Test" );
        TaskDto updated = await UpdateTestTaskAsync( task.Id, "V2 Updated Name", ct, "Changed the name" );

        Assert.AreEqual( 2, updated.CurrentVersionNumber );

        PagedResult<TaskVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions", JsonOptions, ct );

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
    public async Task UpdateTask_SnapshotMatchesState( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "Snapshot Match Test" );
        _ = await UpdateTestTaskAsync( task.Id, "Snapshot Updated", ct );

        PagedResult<TaskVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        TaskVersionDto v2 = versions.Items[0];
        Assert.AreEqual( 2, v2.VersionNumber );

        // Parse the definition JSON to verify it reflects the updated state
        using JsonDocument doc = JsonDocument.Parse( v2.Definition );
        Assert.AreEqual( "Snapshot Updated", doc.RootElement.GetProperty( "name" ).GetString( ) );
        Assert.AreEqual( "echo updated", doc.RootElement.GetProperty( "content" ).GetString( ) );
    }

    // ── GetVersionDiff returns changes ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetVersionDiff_ReturnsChanges( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "Diff Test Original" );
        _ = await UpdateTestTaskAsync( task.Id, "Diff Test Changed", ct );

        PagedResult<TaskVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versions );
        long fromId = versions.Items.First( v => v.VersionNumber == 1 ).Id;
        long toId = versions.Items.First( v => v.VersionNumber == 2 ).Id;

        TaskVersionDiffResponse? diff = await Api.GetFromJsonAsync<TaskVersionDiffResponse>(
            $"/api/v1/tasks/{task.Id}/versions/diff?from={fromId}&to={toId}", JsonOptions, ct );

        Assert.IsNotNull( diff );
        Assert.AreEqual( 1, diff.FromVersionNumber );
        Assert.AreEqual( 2, diff.ToVersionNumber );
        Assert.IsNotEmpty( diff.Changes );

        // Name should be in the diff
        TaskVersionDiffEntry? nameChange = diff.Changes.FirstOrDefault( c => c.PropertyPath == "Name" );
        Assert.IsNotNull( nameChange );
        Assert.AreEqual( "Diff Test Original", nameChange.OldValue );
        Assert.AreEqual( "Diff Test Changed", nameChange.NewValue );
        Assert.AreEqual( DiffChangeType.Modified, nameChange.ChangeType );
    }

    // ── Pagination works correctly ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Pagination_ReturnsCorrectPage( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "Paging Test" );
        // Create 3 more versions (total: 4 including initial)
        for (int i = 2; i <= 4; i++) {
            _ = await UpdateTestTaskAsync( task.Id, $"Paging v{i}", ct, $"Version {i}" );
        }

        // First page of 2
        PagedResult<TaskVersionDto>? page1 = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions?limit=2&offset=0", JsonOptions, ct );

        Assert.IsNotNull( page1 );
        Assert.AreEqual( 4, page1.TotalCount );
        Assert.HasCount( 2, page1.Items );
        Assert.AreEqual( 4, page1.Items[0].VersionNumber ); // Newest first
        Assert.AreEqual( 3, page1.Items[1].VersionNumber );

        // Second page of 2
        PagedResult<TaskVersionDto>? page2 = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions?limit=2&offset=2", JsonOptions, ct );

        Assert.IsNotNull( page2 );
        Assert.AreEqual( 4, page2.TotalCount );
        Assert.HasCount( 2, page2.Items );
        Assert.AreEqual( 2, page2.Items[0].VersionNumber );
        Assert.AreEqual( 1, page2.Items[1].VersionNumber );
    }

    // ── Version belongs to correct task (cross-task isolation) ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Version_BelongsToCorrectTask( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto taskA = await CreateTestTaskAsync( ct, "Isolation A" );
        TaskDto taskB = await CreateTestTaskAsync( ct, "Isolation B" );

        // Each should have exactly 1 version
        PagedResult<TaskVersionDto>? versionsA = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{taskA.Id}/versions", JsonOptions, ct );
        PagedResult<TaskVersionDto>? versionsB = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{taskB.Id}/versions", JsonOptions, ct );

        Assert.IsNotNull( versionsA );
        Assert.IsNotNull( versionsB );
        Assert.AreEqual( 1, versionsA.TotalCount );
        Assert.AreEqual( 1, versionsB.TotalCount );

        // Cannot access taskA's version via taskB's endpoint
        long versionAId = versionsA.Items[0].Id;
        HttpResponseMessage crossResponse = await Api.GetAsync(
            $"/api/v1/tasks/{taskB.Id}/versions/{versionAId}", ct );
        Assert.AreEqual( HttpStatusCode.NotFound, crossResponse.StatusCode );
    }

    // ── Single version detail endpoint ──

    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task GetVersionById_ReturnsCorrectVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        TaskDto task = await CreateTestTaskAsync( ct, "Detail Test" );

        PagedResult<TaskVersionDto>? versions = await Api.GetFromJsonAsync<PagedResult<TaskVersionDto>>(
            $"/api/v1/tasks/{task.Id}/versions", JsonOptions, ct );
        Assert.IsNotNull( versions );
        long versionId = versions.Items[0].Id;

        TaskVersionDto? detail = await Api.GetFromJsonAsync<TaskVersionDto>(
            $"/api/v1/tasks/{task.Id}/versions/{versionId}", JsonOptions, ct );

        Assert.IsNotNull( detail );
        Assert.AreEqual( task.Id, detail.TaskId );
        Assert.AreEqual( 1, detail.VersionNumber );
        Assert.IsFalse( string.IsNullOrWhiteSpace( detail.Definition ) );
    }
}
