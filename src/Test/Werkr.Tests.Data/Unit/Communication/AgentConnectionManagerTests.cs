using Grpc.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Communication;

[TestClass]
public class AgentConnectionManagerTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;
    private AgentConnectionManager _manager = null!;

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

        // Build a minimal DI container so AgentConnectionManager can resolve WerkrDbContext via IServiceScopeFactory
        ServiceCollection services = new( );
        _ = services.AddDbContext<WerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped );

        _serviceProvider = services.BuildServiceProvider( );

        _manager = new AgentConnectionManager(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _manager.Dispose( );
        _serviceProvider.Dispose( );
        _dbContext.Dispose( );
        _connection.Dispose( );
    }

    [TestMethod]
    public async Task GetChannelAsync_ValidConnection_ReturnsChannel( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection resolved) =
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken );

        Assert.IsNotNull( channel );
        Assert.AreEqual( conn.Id, resolved.Id );
        Assert.AreEqual( conn.RemoteUrl, resolved.RemoteUrl );
    }

    [TestMethod]
    public async Task GetChannelAsync_RevokedConnection_Throws( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Revoked );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( async ( ) =>
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task GetChannelAsync_NonExistentConnection_Throws( ) {
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( async ( ) =>
            await _manager.GetChannelAsync( Guid.NewGuid( ), TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task GetChannelAsync_CachesChannels( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel1, _) =
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel2, _) =
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken );

        // Same channel instance should be returned (cached)
        Assert.AreSame( channel1, channel2 );
    }

    [TestMethod]
    public async Task RemoveChannel_DisposesAndRemoves( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel1, _) =
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken );

        _manager.RemoveChannel( conn.Id );

        // Getting channel again should create a new one (different instance)
        (Grpc.Net.Client.GrpcChannel channel2, _) =
            await _manager.GetChannelAsync( conn.Id, TestContext.CancellationToken );

        Assert.AreNotSame( channel1, channel2 );
    }

    [TestMethod]
    public void CreateCallOptions_SetsMetadataCorrectly( ) {
        Guid connId = Guid.NewGuid( );
        Guid callId = Guid.NewGuid( );
        RegisteredConnection conn = new( ) {
            Id = connId,
            ConnectionName = "Test",
            RemoteUrl = "https://localhost:5001",
            OutboundApiKey = "test-api-key",
            InboundApiKeyHash = "hash",
            SharedKey = new byte[32],
            IsServer = true,
            Status = ConnectionStatus.Connected,
        };

        CallOptions options = AgentConnectionManager.CreateCallOptions( conn, callId, TestContext.CancellationToken );

        Assert.IsNotNull( options.Headers );
        Assert.AreEqual( $"Bearer test-api-key", options.Headers.GetValue( "authorization" ) );
        Assert.AreEqual( connId.ToString( ), options.Headers.GetValue( "x-werkr-connection-id" ) );
        Assert.AreEqual( callId.ToString( ), options.Headers.GetValue( "x-werkr-call-id" ) );
    }

    [TestMethod]
    public void CreateCallOptions_SetsDeadline( ) {
        RegisteredConnection conn = new( ) {
            ConnectionName = "Test",
            RemoteUrl = "https://localhost:5001",
            OutboundApiKey = "key",
            InboundApiKeyHash = "hash",
            SharedKey = new byte[32],
            IsServer = true,
            Status = ConnectionStatus.Connected,
        };

        DateTime before = DateTime.UtcNow;
        CallOptions options = AgentConnectionManager.CreateCallOptions(
            conn, cancellationToken: TestContext.CancellationToken, timeout: TimeSpan.FromMinutes( 5 ) );
        DateTime after = DateTime.UtcNow;

        Assert.IsNotNull( options.Deadline );
        Assert.IsGreaterThanOrEqualTo( before.AddMinutes( 5 ), options.Deadline.Value );
        Assert.IsLessThanOrEqualTo( after.AddMinutes( 5 ).AddSeconds( 1 ), options.Deadline.Value );
    }

    private RegisteredConnection SeedServerConnection( ConnectionStatus status ) {
        RSAKeyPair keys = EncryptionProvider.GenerateRSAKeyPair( );

        RegisteredConnection conn = new( ) {
            ConnectionName = "TestAgent",
            RemoteUrl = "https://localhost:5001",
            LocalPublicKey = keys.PublicKey,
            LocalPrivateKey = keys.PrivateKey,
            RemotePublicKey = keys.PublicKey,
            OutboundApiKey = "outbound-key",
            InboundApiKeyHash = "inbound-hash",
            SharedKey = EncryptionProvider.GenerateRandomBytes( 32 ),
            IsServer = true,
            Status = status,
        };

        _ = _dbContext.RegisteredConnections.Add( conn );
        return conn;
    }
}
