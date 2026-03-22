using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Core.Health;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Health;

/// <summary>
/// Unit tests for <see cref="AgentStalenessService"/>. Validates passive staleness
/// detection, notification cleanup, and online/offline hook invocations.
/// </summary>
[TestClass]
public class AgentStalenessServiceTests {
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        string dbName = $"staleness_{Guid.NewGuid( ):N}";
        string connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        _connection = new SqliteConnection( connectionString );
        _connection.Open( );

        ServiceCollection services = new( );
        _ = services.AddDbContext<SqliteWerkrDbContext>( opt => opt.UseSqlite( connectionString ) );
        _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<SqliteWerkrDbContext>( ) );
        _serviceProvider = services.BuildServiceProvider( );

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.Database.EnsureCreated( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider?.Dispose( );
        _connection?.Dispose( );
    }

    private RegisteredConnection SeedAgent(
        string name, ConnectionStatus status, DateTime? lastSeen = null ) {
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection agent = new( ) {
            Id = Guid.NewGuid( ),
            ConnectionName = name,
            RemoteUrl = "https://localhost:5100",
            IsServer = true,
            Status = status,
            LastSeen = lastSeen,
            SharedKey = new byte[32],
            InboundApiKeyHash = "hash",
            OutboundApiKey = "key",
            Tags = [],
            AgentVersion = "1.0.0",
        };
        _ = db.RegisteredConnections.Add( agent );
        _ = db.SaveChanges( );
        return agent;
    }

    private void SeedNotification( Guid connectionId, DateTime expiresUtc ) {
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.PendingAgentNotifications.Add( new PendingAgentNotification {
            ConnectionId = connectionId,
            Channel = "test",
            CreatedUtc = DateTime.UtcNow.AddMinutes( -5 ),
            ExpiresUtc = expiresUtc,
        } );
        _ = db.SaveChanges( );
    }

    /// <summary>
    /// Verifies that agents whose <see cref="RegisteredConnection.LastSeen"/>
    /// exceeds the offline threshold are transitioned to Disconnected.
    /// </summary>
    [TestMethod]
    public async Task SweepAsync_TransitionsStaleAgentsToDisconnected( ) {
        CancellationToken ct = TestContext.CancellationToken;
        TimeSpan threshold = TimeSpan.FromSeconds( 180 );

        RegisteredConnection stale = SeedAgent( "stale-agent",
            ConnectionStatus.Connected, DateTime.UtcNow.AddSeconds( -200 ) );

        IServiceScopeFactory scopeFactory =
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        AgentStalenessService service = new(
            scopeFactory,
            NullLogger<AgentStalenessService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds( 50 ),
            offlineThreshold: threshold );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
        await service.StartAsync( cts.Token );
        await Task.Delay( 200, ct );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection? updated = await db.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == stale.Id, ct );

        Assert.IsNotNull( updated );
        Assert.AreEqual( ConnectionStatus.Disconnected, updated.Status );
    }

    /// <summary>
    /// Verifies that connected agents within the threshold remain Connected.
    /// </summary>
    [TestMethod]
    public async Task SweepAsync_FreshAgentsRemainConnected( ) {
        CancellationToken ct = TestContext.CancellationToken;
        TimeSpan threshold = TimeSpan.FromSeconds( 180 );

        RegisteredConnection fresh = SeedAgent( "fresh-agent",
            ConnectionStatus.Connected, DateTime.UtcNow.AddSeconds( -10 ) );

        IServiceScopeFactory scopeFactory =
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        AgentStalenessService service = new(
            scopeFactory,
            NullLogger<AgentStalenessService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds( 50 ),
            offlineThreshold: threshold );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
        await service.StartAsync( cts.Token );
        await Task.Delay( 200, ct );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection? updated = await db.RegisteredConnections
            .FirstOrDefaultAsync( c => c.Id == fresh.Id, ct );

        Assert.IsNotNull( updated );
        Assert.AreEqual( ConnectionStatus.Connected, updated.Status );
    }

    /// <summary>
    /// Verifies that the <see cref="AgentStalenessService.OnAgentOffline"/> hook
    /// is invoked when an agent transitions to Disconnected.
    /// </summary>
    [TestMethod]
    public async Task SweepAsync_InvokesOnAgentOfflineHook( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection stale = SeedAgent( "hook-agent",
            ConnectionStatus.Connected, DateTime.UtcNow.AddSeconds( -200 ) );

        Guid? capturedId = null;
        string? capturedName = null;

        IServiceScopeFactory scopeFactory =
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        AgentStalenessService service = new(
            scopeFactory,
            NullLogger<AgentStalenessService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds( 50 ),
            offlineThreshold: TimeSpan.FromSeconds( 180 ) ) {
            OnAgentOffline = ( id, name, _ ) => {
                capturedId = id;
                capturedName = name;
                return Task.CompletedTask;
            },
        };

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
        await service.StartAsync( cts.Token );
        await Task.Delay( 200, ct );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        Assert.AreEqual( stale.Id, capturedId );
        Assert.AreEqual( "hook-agent", capturedName );
    }

    /// <summary>
    /// Verifies that expired <see cref="PendingAgentNotification"/> rows are cleaned up.
    /// </summary>
    [TestMethod]
    public async Task SweepAsync_CleansUpExpiredNotifications( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( "cleanup-agent",
            ConnectionStatus.Connected, DateTime.UtcNow );

        SeedNotification( agent.Id, DateTime.UtcNow.AddHours( -1 ) ); // expired
        SeedNotification( agent.Id, DateTime.UtcNow.AddHours( 1 ) );  // still valid

        IServiceScopeFactory scopeFactory =
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        AgentStalenessService service = new(
            scopeFactory,
            NullLogger<AgentStalenessService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds( 50 ),
            offlineThreshold: TimeSpan.FromSeconds( 180 ) );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
        await service.StartAsync( cts.Token );
        await Task.Delay( 200, ct );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        int remaining = await db.PendingAgentNotifications.CountAsync( ct );

        Assert.AreEqual( 1, remaining, "Expired notification should be deleted; valid one retained." );
    }
}
