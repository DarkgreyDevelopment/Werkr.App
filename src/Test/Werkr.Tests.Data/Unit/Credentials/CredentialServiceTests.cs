using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Credentials;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Tests.Data.Unit.Credentials;

/// <summary>
/// Unit tests for <see cref="CredentialService"/>: CRUD, rename cascade, delete reference check,
/// and agent scope resolution.
/// </summary>
[TestClass]
public class CredentialServiceTests {

    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private CredentialService _service = null!;

    /// <summary>MSTest context providing per-test cancellation tokens.</summary>
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        TaskVersionService versionService = new(
            _dbContext,
            new NoopAuditService( ),
            NullLogger<TaskVersionService>.Instance
        );

        _service = new CredentialService(
            _dbContext,
            versionService,
            new NoopAuditService( ),
            NullLogger<CredentialService>.Instance
        );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // ── Helpers ──

    private static CredentialCreateRequest MakeCreateRequest(
        string name = "test-cred",
        string type = "Password",
        string value = "s3cret!",
        string? description = "A test credential",
        IReadOnlyList<Guid>? agentScopeIds = null
    ) => new( name, type, value, description, agentScopeIds );

    private async Task<RegisteredConnection> SeedAgentAsync( Guid? id = null, CancellationToken ct = default ) {
        RegisteredConnection conn = new( ) {
            Id = id ?? Guid.NewGuid( ),
            ConnectionName = "TestAgent",
            RemoteUrl = "https://localhost:5100",
            OutboundApiKey = "test-api-key",
            InboundApiKeyHash = "hash",
            SharedKey = new byte[32],
            IsServer = true,
            Status = ConnectionStatus.Connected,
        };
        _ = _dbContext.RegisteredConnections.Add( conn );
        _ = await _dbContext.SaveChangesAsync( ct );
        return conn;
    }

    private async Task<WerkrTask> SeedTaskWithCredentialAsync(
        string credentialName, CancellationToken ct = default
    ) {
        WerkrTask task = new( ) {
            Name = "Task-With-Cred",
            ActionType = TaskActionType.Action,
            Content = "SendEmail",
            TargetTags = ["linux"],
            ActionParameters = $$"""{"ActionType":"SendEmail","CredentialName":"{{credentialName}}","SmtpHost":"mail.local"}""",
        };
        _ = _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );
        return task;
    }

    // ── Create ──

    /// <summary>Create stores credential and returns plaintext value once.</summary>
    [TestMethod]
    public async Task Create_StoresCredential_ReturnsPlaintextOnce( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse response = await _service.CreateAsync(
            MakeCreateRequest( ), "user1", ct );

        Assert.AreEqual( "test-cred", response.Name );
        Assert.AreEqual( "s3cret!", response.PlaintextValue );
        Assert.IsGreaterThan( 0L, response.Id );

        // Subsequent read returns DTO without value field
        CredentialDto? dto = await _service.GetByIdAsync( response.Id, ct );
        Assert.IsNotNull( dto );
        Assert.AreEqual( "test-cred", dto.Name );
    }

    // ── Update ──

    /// <summary>Updating a credential replaces its value.</summary>
    [TestMethod]
    public async Task Update_ReplacesValue( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse created = await _service.CreateAsync(
            MakeCreateRequest( ), "user1", ct );

        CredentialDto updated = await _service.UpdateAsync(
            created.Id, new CredentialUpdateRequest( Value: "new-secret" ), "user1", ct );

        Assert.AreEqual( created.Id, updated.Id );
        Assert.AreEqual( "test-cred", updated.Name );
    }

    // ── Delete ──

    /// <summary>Delete is blocked when a task references the credential via CredentialName property.</summary>
    [TestMethod]
    public async Task Delete_BlockedWhenTaskReferences( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse created = await _service.CreateAsync(
            MakeCreateRequest( ), "user1", ct );
        _ = await SeedTaskWithCredentialAsync( "test-cred", ct );

        InvalidOperationException ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => _service.DeleteAsync( created.Id, "user1", ct ) );

        Assert.Contains( "Task-With-Cred", ex.Message );
    }

    /// <summary>Delete succeeds when no tasks reference the credential.</summary>
    [TestMethod]
    public async Task Delete_SucceedsWhenNoReferences( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse created = await _service.CreateAsync(
            MakeCreateRequest( ), "user1", ct );

        await _service.DeleteAsync( created.Id, "user1", ct );

        CredentialDto? fromDb = await _service.GetByIdAsync( created.Id, ct );
        Assert.IsNull( fromDb );
    }

    /// <summary>
    /// Delete does NOT false-positive on substring matches. A credential named "smtp"
    /// should not be blocked by a task whose JSON contains "smtpHost" but no CredentialName reference.
    /// </summary>
    [TestMethod]
    public async Task Delete_NoFalsePositiveOnSubstringMatch( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse created = await _service.CreateAsync(
            MakeCreateRequest( name: "smtp" ), "user1", ct );

        // Task JSON contains "smtp" as a substring in SmtpHost but not as a CredentialName
        WerkrTask task = new( ) {
            Name = "Smtp-Task",
            ActionType = TaskActionType.Action,
            Content = "SendEmail",
            TargetTags = ["linux"],
            ActionParameters = """{"ActionType":"SendEmail","SmtpHost":"smtp.example.com","SmtpPort":587}""",
        };
        _ = _dbContext.Tasks.Add( task );
        _ = await _dbContext.SaveChangesAsync( ct );

        // Should succeed despite substring match — JSON-aware check sees no CredentialName reference
        await _service.DeleteAsync( created.Id, "user1", ct );

        CredentialDto? fromDb = await _service.GetByIdAsync( created.Id, ct );
        Assert.IsNull( fromDb );
    }

    // ── Rename ──

    /// <summary>Rename cascades to task ActionParameters.</summary>
    [TestMethod]
    public async Task Rename_CascadesToTaskActionParameters( ) {
        CancellationToken ct = TestContext.CancellationToken;
        CredentialCreateResponse created = await _service.CreateAsync(
            MakeCreateRequest( name: "old-cred" ), "user1", ct );
        WerkrTask task = await SeedTaskWithCredentialAsync( "old-cred", ct );

        CredentialDto renamed = await _service.RenameAsync( created.Id, "new-cred", "user1", ct );

        Assert.AreEqual( "new-cred", renamed.Name );

        // Verify task ActionParameters updated
        WerkrTask? updatedTask = await _dbContext.Tasks.FirstOrDefaultAsync( t => t.Id == task.Id, ct );
        Assert.IsNotNull( updatedTask );
        Assert.Contains( "new-cred", updatedTask.ActionParameters! );
        Assert.DoesNotContain( "old-cred", updatedTask.ActionParameters! );
    }

    // ── Scope resolution ──

    /// <summary>Scoped credential resolves for an in-scope agent.</summary>
    [TestMethod]
    public async Task ResolveForAgent_ReturnsValueForInScopeAgent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = await SeedAgentAsync( ct: ct );
        _ = await _service.CreateAsync(
            MakeCreateRequest( agentScopeIds: [agent.Id] ), "user1", ct );

        CredentialResolveResult result = await _service.ResolveForAgentAsync(
            "test-cred", agent.Id, "system", ct );

        Assert.IsTrue( result.Found );
        Assert.IsTrue( result.InScope );
        Assert.IsNotNull( result.DecryptedValue );
    }

    /// <summary>Scoped credential returns Found=true, InScope=false for an out-of-scope agent.</summary>
    [TestMethod]
    public async Task ResolveForAgent_DistinguishesOutOfScope( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection scopedAgent = await SeedAgentAsync( ct: ct );
        Guid outOfScopeAgentId = Guid.NewGuid( );

        _ = await _service.CreateAsync(
            MakeCreateRequest( agentScopeIds: [scopedAgent.Id] ), "user1", ct );

        CredentialResolveResult result = await _service.ResolveForAgentAsync(
            "test-cred", outOfScopeAgentId, "system", ct );

        Assert.IsTrue( result.Found );
        Assert.IsFalse( result.InScope );
        Assert.IsNull( result.DecryptedValue );
    }

    /// <summary>Non-existent credential returns Found=false.</summary>
    [TestMethod]
    public async Task ResolveForAgent_ReturnsNotFoundForMissing( ) {
        CancellationToken ct = TestContext.CancellationToken;

        CredentialResolveResult result = await _service.ResolveForAgentAsync(
            "nonexistent", Guid.NewGuid( ), "system", ct );

        Assert.IsFalse( result.Found );
        Assert.IsFalse( result.InScope );
        Assert.IsNull( result.DecryptedValue );
    }

    /// <summary>Unscoped credential (no agent scopes) is available to any agent.</summary>
    [TestMethod]
    public async Task UnscopedCredential_AvailableToAllAgents( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = await _service.CreateAsync( MakeCreateRequest( ), "user1", ct );

        CredentialResolveResult result = await _service.ResolveForAgentAsync(
            "test-cred", Guid.NewGuid( ), "system", ct );

        Assert.IsTrue( result.Found );
        Assert.IsTrue( result.InScope );
        Assert.IsNotNull( result.DecryptedValue );
    }

    /// <summary>No-op audit service for unit tests that don't need audit logging.</summary>
    private sealed class NoopAuditService : IAuditService {
        public Task LogAsync( AuditEntry entry, CancellationToken ct = default ) => Task.CompletedTask;
        public Task<PagedResult<AuditEventDto>> QueryAsync( AuditQuery query, CancellationToken ct = default ) =>
            Task.FromResult( new PagedResult<AuditEventDto>( [], 0, 25, 0 ) );
        public Task ExportAsync( AuditQuery query, ExportFormat format, Stream outputStream, CancellationToken ct = default, int? maxRows = null ) =>
            Task.CompletedTask;
    }
}
