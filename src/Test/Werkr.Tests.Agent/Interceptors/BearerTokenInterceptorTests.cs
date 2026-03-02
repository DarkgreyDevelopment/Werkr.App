using Grpc.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Agent.Interceptors;
using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Interceptors;

[TestClass]
public class BearerTokenInterceptorTests {
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private BearerTokenInterceptor _interceptor = null!;

    /// <summary>Raw API key value that matches the stored hash.</summary>
    private string _rawToken = null!;
    private Guid _connectionId;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        ServiceCollection services = new( );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ), ServiceLifetime.Scoped );
        _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<SqliteWerkrDbContext>( ) );

        _serviceProvider = services.BuildServiceProvider( );

        // Ensure schema is created
        using IServiceScope initScope = _serviceProvider.CreateScope( );
        WerkrDbContext initDb = initScope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = initDb.Database.EnsureCreated( );

        // Seed a valid agent-side connection (IsServer = false)
        _rawToken = Convert.ToBase64String( EncryptionProvider.GenerateRandomBytes( 32 ) );
        string tokenHash = EncryptionProvider.HashSHA512String( _rawToken );
        RSAKeyPair keys = EncryptionProvider.GenerateRSAKeyPair( );

        RegisteredConnection conn = new( ) {
            ConnectionName = "TestAgent",
            RemoteUrl = "https://localhost:5000",
            LocalPublicKey = keys.PublicKey,
            LocalPrivateKey = keys.PrivateKey,
            RemotePublicKey = keys.PublicKey,
            OutboundApiKey = "outbound",
            InboundApiKeyHash = tokenHash,
            SharedKey = EncryptionProvider.GenerateRandomBytes( 32 ),
            IsServer = false,
            Status = ConnectionStatus.Connected,
        };

        _ = initDb.RegisteredConnections.Add( conn );
        _ = initDb.SaveChanges( );
        _connectionId = conn.Id;

        _interceptor = new BearerTokenInterceptor(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<BearerTokenInterceptor>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider.Dispose( );
        _connection.Dispose( );
    }

    [TestMethod]
    public async Task ValidToken_InvokesContinuation( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );
        bool continuationCalled = false;

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( req, context ) => {
                continuationCalled = true;
                return Task.FromResult( "response" );
            } );

        Assert.IsTrue( continuationCalled );
    }

    [TestMethod]
    public async Task ValidToken_StoresConnectionInUserState( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( req, context ) => Task.FromResult( "response" ) );

        Assert.IsTrue( ctx.ExposedUserState.ContainsKey( "Connection" ) );
        RegisteredConnection resolved = (RegisteredConnection)ctx.ExposedUserState["Connection"];
        Assert.AreEqual( _connectionId, resolved.Id );
    }

    [TestMethod]
    public async Task MissingAuthHeader_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "x-werkr-connection-id", _connectionId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request", ctx, ( r, c ) => Task.FromResult( "ok" ) ) );

        Assert.AreEqual( StatusCode.Unauthenticated, ex.StatusCode );
    }

    [TestMethod]
    public async Task MissingConnectionIdHeader_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
        };
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request", ctx, ( r, c ) => Task.FromResult( "ok" ) ) );

        Assert.AreEqual( StatusCode.Unauthenticated, ex.StatusCode );
    }

    [TestMethod]
    public async Task InvalidToken_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", "Bearer wrong-token" },
            { "x-werkr-connection-id", _connectionId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request", ctx, ( r, c ) => Task.FromResult( "ok" ) ) );

        Assert.AreEqual( StatusCode.Unauthenticated, ex.StatusCode );
    }

    [TestMethod]
    public async Task RevokedConnection_ThrowsUnauthenticated( ) {
        // Revoke the connection
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection? conn = await db.RegisteredConnections.FindAsync(
            [ _connectionId ], TestContext.CancellationToken );
        conn!.Status = ConnectionStatus.Revoked;
        _ = await db.SaveChangesAsync( TestContext.CancellationToken );

        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request", ctx, ( r, c ) => Task.FromResult( "ok" ) ) );

        Assert.AreEqual( StatusCode.Unauthenticated, ex.StatusCode );
    }

    [TestMethod]
    public async Task NonExistentConnection_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
            { "x-werkr-connection-id", Guid.NewGuid( ).ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request", ctx, ( r, c ) => Task.FromResult( "ok" ) ) );

        Assert.AreEqual( StatusCode.Unauthenticated, ex.StatusCode );
    }

    [TestMethod]
    public async Task ValidToken_UpdatesLastSeen( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request", ctx, ( r, c ) => Task.FromResult( "ok" ) );

        // Verify LastSeen was set
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection? conn = await db.RegisteredConnections.FindAsync(
            [ _connectionId ], TestContext.CancellationToken );
        Assert.IsNotNull( conn!.LastSeen );
        Assert.IsGreaterThanOrEqualTo( DateTime.UtcNow.AddSeconds( -5 ), conn.LastSeen.Value );
    }

    [TestMethod]
    public async Task CallId_StoredInUserState( ) {
        Guid callId = Guid.NewGuid( );
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
            { "x-werkr-connection-id", _connectionId.ToString( ) },
            { "x-werkr-call-id", callId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create( headers, TestContext.CancellationToken );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request", ctx, ( r, c ) => Task.FromResult( "ok" ) );

        Assert.IsTrue( ctx.ExposedUserState.ContainsKey( "CallId" ) );
        Assert.AreEqual( callId.ToString( ), ctx.ExposedUserState["CallId"] );
    }

    private Metadata CreateValidHeaders( ) {
        return [
            new Metadata.Entry( "authorization", $"Bearer {_rawToken}" ),
            new Metadata.Entry( "x-werkr-connection-id", _connectionId.ToString( ) ),
        ];
    }
}
