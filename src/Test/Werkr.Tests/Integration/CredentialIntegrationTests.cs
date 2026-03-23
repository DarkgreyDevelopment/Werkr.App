using System.Net.Http.Json;
using System.Text.Json;
using Werkr.Common.Models;

namespace Werkr.Tests.Integration;

[TestClass]
public class CredentialIntegrationTests {
    public TestContext TestContext { get; set; } = null!;

    private static JsonSerializerOptions JsonOptions => AppHostFixture.JsonOptions;
    private static HttpClient Api => AppHostFixture.ApiClient;

    private async Task<CredentialCreateResponse> CreateTestCredentialAsync(
        CancellationToken ct, string name = "int-test-cred"
    ) {
        CredentialCreateRequest request = new(
            Name: $"{name}-{Guid.NewGuid( ):N}",
            Type: "Password",
            Value: "test-secret-value",
            Description: "Integration test credential" );

        HttpResponseMessage response = await Api.PostAsJsonAsync(
            "/api/v1/settings/credentials", request, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, response.StatusCode );

        CredentialCreateResponse? created = await response.Content
            .ReadFromJsonAsync<CredentialCreateResponse>( JsonOptions, ct );
        Assert.IsNotNull( created );
        return created;
    }

    /// <summary>POST returns plaintext once; subsequent GET returns no value field.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Create_ReturnsPlaintextOnce( ) {
        CancellationToken ct = TestContext.CancellationToken;

        CredentialCreateResponse created = await CreateTestCredentialAsync( ct );
        Assert.AreEqual( "test-secret-value", created.PlaintextValue );

        // GET list — values are never returned
        List<CredentialDto>? all = await Api.GetFromJsonAsync<List<CredentialDto>>(
            "/api/v1/settings/credentials", JsonOptions, ct );
        Assert.IsNotNull( all );

        CredentialDto? fetched = all.FirstOrDefault( c => c.Id == created.Id );
        Assert.IsNotNull( fetched );
        Assert.AreEqual( created.Name, fetched.Name );
        // CredentialDto has no Value property — structural masking
    }

    /// <summary>DELETE returns 409 when tasks reference the credential.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Delete_Returns409WhenReferenced( ) {
        CancellationToken ct = TestContext.CancellationToken;

        CredentialCreateResponse cred = await CreateTestCredentialAsync( ct, "ref-test" );

        // Create a task that references this credential by name
        TaskCreateRequest taskRequest = new(
            Name: $"Ref-Task-{Guid.NewGuid( ):N}",
            Description: "Task referencing a credential",
            ActionType: "Action",
            Content: "",
            Arguments: null,
            TargetTags: ["test"],
            ActionSubType: "SendEmail",
            ActionParameters: $$"""{"SmtpHost":"mail.local","From":"test@test.com","To":["a@b.com"],"Subject":"test","CredentialName":"{{cred.Name}}"}""" );

        HttpResponseMessage taskResponse = await Api.PostAsJsonAsync( "/api/v1/tasks", taskRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, taskResponse.StatusCode );

        // Attempt delete — should fail with 409
        HttpResponseMessage deleteResponse = await Api.DeleteAsync(
            $"/api/v1/settings/credentials/{cred.Id}", ct );
        Assert.AreEqual( HttpStatusCode.Conflict, deleteResponse.StatusCode );
    }

    /// <summary>Rename cascades to task ActionParameters.</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task Rename_CascadesToTasks( ) {
        CancellationToken ct = TestContext.CancellationToken;

        CredentialCreateResponse cred = await CreateTestCredentialAsync( ct, "rename-test" );
        string newName = $"renamed-{Guid.NewGuid( ):N}";

        // Create task referencing old name
        TaskCreateRequest taskRequest = new(
            Name: $"Rename-Task-{Guid.NewGuid( ):N}",
            Description: "Task for rename test",
            ActionType: "Action",
            Content: "",
            Arguments: null,
            TargetTags: ["test"],
            ActionSubType: "SendEmail",
            ActionParameters: $$"""{"SmtpHost":"mail.local","From":"test@test.com","To":["a@b.com"],"Subject":"test","CredentialName":"{{cred.Name}}"}""" );

        HttpResponseMessage taskResponse = await Api.PostAsJsonAsync( "/api/v1/tasks", taskRequest, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.Created, taskResponse.StatusCode );
        TaskDto? task = await taskResponse.Content.ReadFromJsonAsync<TaskDto>( JsonOptions, ct );
        Assert.IsNotNull( task );

        // Rename credential
        CredentialRenameRequest renameReq = new( newName );
        HttpResponseMessage renameResponse = await Api.PutAsJsonAsync(
            $"/api/v1/settings/credentials/{cred.Id}/name", renameReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.OK, renameResponse.StatusCode );

        // Verify task updated
        TaskDto? updatedTask = await Api.GetFromJsonAsync<TaskDto>(
            $"/api/v1/tasks/{task.Id}", JsonOptions, ct );
        Assert.IsNotNull( updatedTask );
        Assert.IsNotNull( updatedTask.ActionParameters );
        Assert.Contains( newName, updatedTask.ActionParameters );
    }

    /// <summary>Scope management updates and returns correctly (empty scope = all agents).</summary>
    [TestMethod]
    [Timeout( 60_000, CooperativeCancellation = true )]
    public async Task ScopeManagement_Works( ) {
        CancellationToken ct = TestContext.CancellationToken;

        CredentialCreateResponse cred = await CreateTestCredentialAsync( ct, "scope-test" );

        // Update scopes with an empty list (unrestricted — available to all agents)
        CredentialScopeUpdateRequest scopeReq = new( [] );
        HttpResponseMessage scopeResponse = await Api.PutAsJsonAsync(
            $"/api/v1/settings/credentials/{cred.Id}/scope", scopeReq, JsonOptions, ct );
        Assert.AreEqual( HttpStatusCode.NoContent, scopeResponse.StatusCode );

        // Verify scopes are empty
        List<CredentialDto>? all = await Api.GetFromJsonAsync<List<CredentialDto>>(
            "/api/v1/settings/credentials", JsonOptions, ct );
        Assert.IsNotNull( all );
        CredentialDto? updated = all.FirstOrDefault( c => c.Id == cred.Id );
        Assert.IsNotNull( updated );
        Assert.IsEmpty( updated.AgentScopeIds );
    }
}
