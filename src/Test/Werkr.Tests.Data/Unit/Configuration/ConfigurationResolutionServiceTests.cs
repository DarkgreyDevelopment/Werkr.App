using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Configuration;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Tests.Data.Unit.Configuration;

/// <summary>
/// Unit tests for <see cref="ConfigurationResolutionService"/>: hierarchical override resolution,
/// change logging, sync version, and validation.
/// </summary>
[TestClass]
public class ConfigurationResolutionServiceTests {

    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private ConfigurationResolutionService _service = null!;

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

        _service = new ConfigurationResolutionService(
            _dbContext,
            new NoopAuditService( ),
            NullLogger<ConfigurationResolutionService>.Instance
        );

        // Seed a global config entry for tests
        _ = _dbContext.ConfigurationEntries.Add( new ConfigurationEntry {
            Key = "test.setting",
            Value = "global-value",
            ValueType = "string",
            Category = "server",
            Description = "A test setting.",
            ScopeLevel = 0,
            ScopeId = null,
            SyncVersion = 1,
            DefaultValue = "global-value",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = "system",
        } );
        _ = _dbContext.ConfigurationEntries.Add( new ConfigurationEntry {
            Key = "test.number",
            Value = "10",
            ValueType = "number",
            Category = "agent",
            Description = "A numeric setting.",
            ScopeLevel = 0,
            ScopeId = null,
            SyncVersion = 2,
            ValidationRules = """{"min":5,"max":100}""",
            DefaultValue = "10",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = "system",
        } );
        _ = _dbContext.SaveChanges( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // ── GetEffectiveValue ──

    /// <summary>Returns global value when no agent override exists.</summary>
    [TestMethod]
    public async Task GetEffectiveValue_ReturnsGlobal_WhenNoOverride( ) {
        CancellationToken ct = TestContext.CancellationToken;

        EffectiveConfigurationDto? result = await _service.GetEffectiveValueAsync( "test.setting", null, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( "global-value", result.EffectiveValue );
        Assert.AreEqual( "Global", result.Source );
    }

    /// <summary>Returns agent override when present.</summary>
    [TestMethod]
    public async Task GetEffectiveValue_ReturnsAgentOverride_WhenPresent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string agentId = Guid.NewGuid( ).ToString( );

        // Create an agent-scoped override
        _ = _dbContext.ConfigurationEntries.Add( new ConfigurationEntry {
            Key = "test.setting",
            Value = "agent-value",
            ValueType = "string",
            Category = "server",
            Description = "A test setting.",
            ScopeLevel = 1,
            ScopeId = agentId,
            SyncVersion = 3,
            DefaultValue = "global-value",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = "user1",
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        EffectiveConfigurationDto? result = await _service.GetEffectiveValueAsync( "test.setting", agentId, ct );

        Assert.IsNotNull( result );
        Assert.AreEqual( "agent-value", result.EffectiveValue );
        Assert.AreEqual( "Agent Override", result.Source );
        Assert.AreEqual( "global-value", result.GlobalValue );
        Assert.AreEqual( "agent-value", result.OverrideValue );
    }

    /// <summary>GetEffectiveSettings merges global and agent overrides correctly.</summary>
    [TestMethod]
    public async Task GetEffectiveSettings_MergesGlobalAndOverrides( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string agentId = Guid.NewGuid( ).ToString( );

        // Override only test.setting, not test.number
        _ = _dbContext.ConfigurationEntries.Add( new ConfigurationEntry {
            Key = "test.setting",
            Value = "override-val",
            ValueType = "string",
            Category = "server",
            ScopeLevel = 1,
            ScopeId = agentId,
            SyncVersion = 3,
            DefaultValue = "global-value",
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = "user1",
        } );
        _ = await _dbContext.SaveChangesAsync( ct );

        IReadOnlyList<EffectiveConfigurationDto> results = await _service.GetEffectiveSettingsAsync( agentId, ct );

        Assert.HasCount( 2, results );

        EffectiveConfigurationDto overridden = results.First( r => r.Key == "test.setting" );
        Assert.AreEqual( "Agent Override", overridden.Source );
        Assert.AreEqual( "override-val", overridden.EffectiveValue );

        EffectiveConfigurationDto global = results.First( r => r.Key == "test.number" );
        Assert.AreEqual( "Global", global.Source );
        Assert.AreEqual( "10", global.EffectiveValue );
    }

    // ── Update ──

    /// <summary>Update creates a change log entry with before/after values.</summary>
    [TestMethod]
    public async Task Update_CreatesChangeLogEntry( ) {
        CancellationToken ct = TestContext.CancellationToken;

        _ = await _service.UpdateAsync(
            "test.setting", new ConfigurationUpdateRequest( "new-value" ), "user1", ct );

        IReadOnlyList<ConfigurationChangeLogDto> history =
            await _service.GetHistoryAsync( "test.setting", 10, ct );

        Assert.HasCount( 1, history );
        Assert.AreEqual( "global-value", history[0].PreviousValue );
        Assert.AreEqual( "new-value", history[0].NewValue );
        Assert.AreEqual( "user1", history[0].ChangedByUserId );
    }

    /// <summary>Update increments SyncVersion.</summary>
    [TestMethod]
    public async Task Update_IncrementsSyncVersion( ) {
        CancellationToken ct = TestContext.CancellationToken;
        long versionBefore = await _service.GetCurrentVersionAsync( ct );

        _ = await _service.UpdateAsync(
            "test.setting", new ConfigurationUpdateRequest( "changed" ), "user1", ct );

        long versionAfter = await _service.GetCurrentVersionAsync( ct );
        Assert.IsGreaterThan( versionBefore, versionAfter );
    }

    /// <summary>Update rejects invalid value per ValidationRules.</summary>
    [TestMethod]
    public async Task Update_ValidatesAgainstRules( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // test.number has min=5 — try setting to 3
        ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            ( ) => _service.UpdateAsync(
                "test.number", new ConfigurationUpdateRequest( "3" ), "user1", ct ) );

        Assert.Contains( ">= 5", ex.Message );
    }

    // ── GetDelta ──

    /// <summary>GetDelta returns only entries newer than the given version.</summary>
    [TestMethod]
    public async Task GetDelta_ReturnsOnlyNewerEntries( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Current max version is 2 (from seed)
        _ = await _service.UpdateAsync(
            "test.setting", new ConfigurationUpdateRequest( "updated" ), "user1", ct );

        // Delta from version 2 should return only the updated entry (version 3)
        IReadOnlyList<ConfigurationEntryDto> delta = await _service.GetDeltaAsync( 2, null, ct );

        Assert.HasCount( 1, delta );
        Assert.AreEqual( "test.setting", delta[0].Key );
        Assert.AreEqual( "updated", delta[0].Value );
    }

    /// <summary>No-op audit service for unit tests.</summary>
    private sealed class NoopAuditService : IAuditService {
        public Task LogAsync( AuditEntry entry, CancellationToken ct = default ) => Task.CompletedTask;
        public Task<PagedResult<AuditEventDto>> QueryAsync( AuditQuery query, CancellationToken ct = default ) =>
            Task.FromResult( new PagedResult<AuditEventDto>( [], 0, 25, 0 ) );
        public Task ExportAsync( AuditQuery query, ExportFormat format, Stream outputStream, CancellationToken ct = default, int? maxRows = null ) =>
            Task.CompletedTask;
    }
}
