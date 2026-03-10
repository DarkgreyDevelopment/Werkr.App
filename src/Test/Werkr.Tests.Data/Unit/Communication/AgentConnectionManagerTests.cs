using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Contains unit tests for the <see cref="AgentConnectionManager"/> class defined in Werkr.Core. Validates gRPC channel
/// retrieval, caching, removal, call option metadata, and connection status enforcement using an in-memory SQLite
/// database.
/// </summary>
[TestClass]
public class AgentConnectionManagerTests {
    /// <summary>
    /// The in-memory SQLite connection kept open for the duration of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The <see cref="SqliteWerkrDbContext"/> used for seeding and querying test data.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The service provider supplying scoped <see cref="WerkrDbContext"/> instances.
    /// </summary>
    private ServiceProvider _serviceProvider = null!;
    /// <summary>
    /// The <see cref="AgentConnectionManager"/> instance under test.
    /// </summary>
    private AgentConnectionManager _manager = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database, registers <see cref="WerkrDbContext"/> services, and constructs the <see
    /// cref="AgentConnectionManager"/> under test.
    /// </summary>
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
            ServiceLifetime.Scoped
        );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped
        );

        _serviceProvider = services.BuildServiceProvider( );

        _manager = new AgentConnectionManager(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance
        );
    }

    /// <summary>
    /// Disposes the manager, service provider, database context, and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _manager.Dispose( );
        _serviceProvider.Dispose( );
        _dbContext.Dispose( );
        _connection.Dispose( );
    }

    /// <summary>
    /// Verifies that <see cref="GetChannelAsync"/> returns a non-null gRPC channel and the matching <see
    /// cref="RegisteredConnection"/> for a connected agent.
    /// </summary>
    [TestMethod]
    public async Task GetChannelAsync_ValidConnection_ReturnsChannel( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection resolved) =
            await _manager.GetChannelAsync(
                conn.Id,
                TestContext.CancellationToken
            );

        Assert.IsNotNull( channel );
        Assert.AreEqual(
            conn.Id,
            resolved.Id
        );
        Assert.AreEqual(
            conn.RemoteUrl,
            resolved.RemoteUrl
        );
    }

    /// <summary>
    /// Verifies that <see cref="GetChannelAsync"/> throws <see cref="InvalidOperationException"/> when the connection
    /// has been revoked.
    /// </summary>
    [TestMethod]
    public async Task GetChannelAsync_RevokedConnection_Throws( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Revoked );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( async ( ) => await _manager.GetChannelAsync(
            conn.Id,
            TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that <see cref="GetChannelAsync"/> throws <see cref="InvalidOperationException"/> when the requested
    /// connection ID does not exist in the database.
    /// </summary>
    [TestMethod]
    public async Task GetChannelAsync_NonExistentConnection_Throws( ) {
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( async ( ) => await _manager.GetChannelAsync(
            Guid.NewGuid( ),
            TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that successive calls to <see cref="GetChannelAsync"/> for the same connection return the same cached
    /// gRPC channel instance.
    /// </summary>
    [TestMethod]
    public async Task GetChannelAsync_CachesChannels( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel1, _) =
            await _manager.GetChannelAsync(
                conn.Id,
                TestContext.CancellationToken
            );

        (Grpc.Net.Client.GrpcChannel channel2, _) =
            await _manager.GetChannelAsync(
                conn.Id,
                TestContext.CancellationToken
            );

        // Same channel instance should be returned (cached)
        Assert.AreSame(
            channel1,
            channel2
        );
    }

    /// <summary>
    /// Verifies that <see cref="RemoveChannel"/> disposes the cached channel so that a subsequent <see
    /// cref="GetChannelAsync"/> call creates a new channel instance.
    /// </summary>
    [TestMethod]
    public async Task RemoveChannel_DisposesAndRemoves( ) {
        RegisteredConnection conn = SeedServerConnection( ConnectionStatus.Connected );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        (Grpc.Net.Client.GrpcChannel channel1, _) =
            await _manager.GetChannelAsync(
                conn.Id,
                TestContext.CancellationToken
            );

        _manager.RemoveChannel( conn.Id );

        // Getting channel again should create a new one (different instance)
        (Grpc.Net.Client.GrpcChannel channel2, _) =
            await _manager.GetChannelAsync(
                conn.Id,
                TestContext.CancellationToken
            );

        Assert.AreNotSame(
            channel1,
            channel2
        );
    }

    /// <summary>
    /// Verifies that <see cref="CreateCallOptions"/> sets the authorization, connection ID, and call ID metadata
    /// headers correctly on the <see cref="CallOptions"/>.
    /// </summary>
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

        CallOptions options = AgentConnectionManager.CreateCallOptions(
            conn,
            callId,
            TestContext.CancellationToken
        );

        Assert.IsNotNull( options.Headers );
        Assert.AreEqual(
            $"Bearer test-api-key",
            options.Headers.GetValue( "authorization" )
        );
        Assert.AreEqual(
            connId.ToString( ),
            options.Headers.GetValue( "x-werkr-connection-id" )
        );
        Assert.AreEqual(
            callId.ToString( ),
            options.Headers.GetValue( "x-werkr-call-id" )
        );
    }

    /// <summary>
    /// Verifies that <see cref="CreateCallOptions"/> sets a deadline on the <see cref="CallOptions"/> when a timeout
    /// value is specified.
    /// </summary>
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
            conn,
            cancellationToken: TestContext.CancellationToken,
            timeout: TimeSpan.FromMinutes( 5 )
        );
        DateTime after = DateTime.UtcNow;

        Assert.IsNotNull( options.Deadline );
        Assert.IsGreaterThanOrEqualTo(
            before.AddMinutes( 5 ),
            options.Deadline.Value
        );
        Assert.IsLessThanOrEqualTo(
            after.AddMinutes( 5 ).AddSeconds( 1 ),
            options.Deadline.Value
        );
    }

    /// <summary>
    /// Creates and persists a <see cref="RegisteredConnection"/> with the specified status and generated RSA keys.
    /// </summary>
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
