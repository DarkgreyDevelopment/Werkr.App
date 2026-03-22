using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Unit tests for <see cref="AgentNotificationService"/>. Validates enqueue,
/// deduplication, broadcast, and TTL default behavior using in-memory SQLite.
/// </summary>
[TestClass]
public class AgentNotificationServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private AgentNotificationService _service = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options =
            new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
                .UseSqlite( _connection )
                .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        _service = new AgentNotificationService(
            NullLogger<AgentNotificationService>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    private RegisteredConnection SeedAgent( string name = "agent-1",
        ConnectionStatus status = ConnectionStatus.Connected ) {
        RegisteredConnection agent = new( ) {
            Id = Guid.NewGuid( ),
            ConnectionName = name,
            RemoteUrl = "https://localhost:5100",
            IsServer = true,
            Status = status,
            SharedKey = new byte[32],
            InboundApiKeyHash = "hash",
            OutboundApiKey = "key",
            Tags = [],
            AgentVersion = "1.0.0",
        };
        _ = _dbContext.RegisteredConnections.Add( agent );
        _ = _dbContext.SaveChanges( );
        return agent;
    }

    /// <summary>
    /// Verifies that <see cref="AgentNotificationService.EnqueueAsync"/> writes a
    /// row with the correct Channel, Payload, and timestamps.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WritesCorrectRow( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );

        await _service.EnqueueAsync( _dbContext, agent.Id, "schedule_invalidation",
            "payload-123", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        PendingAgentNotification? row = await _dbContext.PendingAgentNotifications
            .FirstOrDefaultAsync( ct );

        Assert.IsNotNull( row );
        Assert.AreEqual( agent.Id, row.ConnectionId );
        Assert.AreEqual( "schedule_invalidation", row.Channel );
        Assert.AreEqual( "payload-123", row.Payload );
        Assert.IsGreaterThan( DateTime.UtcNow, row.ExpiresUtc );
    }

    /// <summary>
    /// Verifies that a second enqueue for the same (ConnectionId, Channel) is
    /// silently skipped (deduplication).
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_Deduplicates_SameConnectionAndChannel( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );

        await _service.EnqueueAsync( _dbContext, agent.Id, "config_update", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        await _service.EnqueueAsync( _dbContext, agent.Id, "config_update", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.PendingAgentNotifications.CountAsync( ct );
        Assert.AreEqual( 1, count, "Second enqueue with same (ConnectionId, Channel) should be skipped." );
    }

    /// <summary>
    /// Verifies that different channels for the same agent are not deduplicated.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_AllowsDifferentChannels_ForSameAgent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );

        await _service.EnqueueAsync( _dbContext, agent.Id, "config_update", ct: ct );
        await _service.EnqueueAsync( _dbContext, agent.Id, "schedule_invalidation", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.PendingAgentNotifications.CountAsync( ct );
        Assert.AreEqual( 2, count, "Different channels should not be deduplicated." );
    }

    /// <summary>
    /// Verifies that <see cref="AgentNotificationService.EnqueueForAllAsync"/>
    /// creates one row per connected (non-revoked) agent.
    /// </summary>
    [TestMethod]
    public async Task EnqueueForAllAsync_CreatesRowPerConnectedAgent( ) {
        CancellationToken ct = TestContext.CancellationToken;
        _ = SeedAgent( "agent-1", ConnectionStatus.Connected );
        _ = SeedAgent( "agent-2", ConnectionStatus.Connected );
        _ = SeedAgent( "agent-revoked", ConnectionStatus.Revoked );

        await _service.EnqueueForAllAsync( _dbContext, "workflow_disabled",
            "wf-42", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        int count = await _dbContext.PendingAgentNotifications.CountAsync( ct );
        Assert.AreEqual( 2, count, "Should enqueue for connected agents only, not revoked." );
    }

    /// <summary>
    /// Verifies that the key_rotation channel uses a 24-hour TTL default.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_KeyRotationChannel_Uses24HourTtl( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );

        await _service.EnqueueAsync( _dbContext, agent.Id, "key_rotation", ct: ct );
        _ = await _dbContext.SaveChangesAsync( ct );

        PendingAgentNotification? row = await _dbContext.PendingAgentNotifications
            .FirstOrDefaultAsync( ct );
        Assert.IsNotNull( row );

        // TTL should be ~24 hours (allow 1-minute tolerance for test execution time)
        TimeSpan ttl = row.ExpiresUtc - row.CreatedUtc;
        Assert.IsTrue( ttl.TotalHours > 23.9 && ttl.TotalHours < 24.1,
            $"key_rotation TTL should be ~24h but was {ttl.TotalHours:F1}h." );
    }
}
