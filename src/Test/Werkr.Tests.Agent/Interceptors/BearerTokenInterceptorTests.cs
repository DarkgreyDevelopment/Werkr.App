using Werkr.Agent.Interceptors;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Tests.Agent.Helpers;

namespace Werkr.Tests.Agent.Interceptors;

/// <summary>
/// Unit tests for the <see cref="BearerTokenInterceptor"/> gRPC server interceptor.
/// Verifies that valid bearer tokens with a matching connection-id header allow requests
/// through, while missing, invalid, or revoked credentials produce <see cref="RpcException"/>
/// with <see cref="StatusCode.Unauthenticated"/>. Also confirms side-effects such as storing
/// the <see cref="RegisteredConnection"/> in user state and updating the
/// <see cref="RegisteredConnection.LastSeen"/> timestamp. Uses an in-memory SQLite database
/// for the <see cref="WerkrDbContext"/>.
/// </summary>
[TestClass]
public class BearerTokenInterceptorTests {
    /// <summary>
    /// In-memory SQLite connection kept open for the lifetime of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// DI service provider holding the scoped <see cref="WerkrDbContext"/> registrations.
    /// </summary>
    private ServiceProvider _serviceProvider = null!;
    /// <summary>
    /// The interceptor instance under test.
    /// </summary>
    private BearerTokenInterceptor _interceptor = null!;

    /// <summary>Raw API key value that matches the stored hash.</summary>
    private string _rawToken = null!;
    /// <summary>
    /// The identifier of the pre-seeded <see cref="RegisteredConnection"/> row.
    /// </summary>
    private Guid _connectionId;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes an in-memory SQLite database, seeds a connected <see cref="RegisteredConnection"/>
    /// with a hashed bearer token, and creates the <see cref="BearerTokenInterceptor"/> under test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        ServiceCollection services = new( );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped
        );
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
            NullLogger<BearerTokenInterceptor>.Instance
        );
    }

    /// <summary>
    /// Disposes the DI service provider and the SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider.Dispose( );
        _connection.Dispose( );
    }

    /// <summary>
    /// Verifies that a request with a valid bearer token and connection-id invokes the continuation delegate,
    /// allowing the request to proceed.
    /// </summary>
    [TestMethod]
    public async Task ValidToken_InvokesContinuation( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );
        bool continuationCalled = false;

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( req, context ) => {
                continuationCalled = true;
                return Task.FromResult( "response" );
            }
        );

        Assert.IsTrue( continuationCalled );
    }

    /// <summary>
    /// Verifies that after a valid authentication the interceptor stores the resolved
    /// <see cref="RegisteredConnection"/> in the call context's user state under the
    /// "Connection" key, and that the stored connection matches the expected identifier.
    /// </summary>
    [TestMethod]
    public async Task ValidToken_StoresConnectionInUserState( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( req, context ) => Task.FromResult( "response" )
        );

        Assert.IsTrue( ctx.ExposedUserState.ContainsKey( "Connection" ) );
        RegisteredConnection resolved = (RegisteredConnection)ctx.ExposedUserState["Connection"];
        Assert.AreEqual(
            _connectionId,
            resolved.Id
        );
    }

    /// <summary>
    /// Verifies that a request missing the "authorization" header throws an <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/>.
    /// </summary>
    [TestMethod]
    public async Task MissingAuthHeader_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "x-werkr-connection-id", _connectionId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                ctx,
                ( r, c ) => Task.FromResult( "ok" )
            )
        );

        Assert.AreEqual(
            StatusCode.Unauthenticated,
            ex.StatusCode
        );
    }

    /// <summary>
    /// Verifies that a request missing the "x-werkr-connection-id" header throws an <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/>.
    /// </summary>
    [TestMethod]
    public async Task MissingConnectionIdHeader_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
        };
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                ctx,
                ( r, c ) => Task.FromResult( "ok" )
            )
        );

        Assert.AreEqual(
            StatusCode.Unauthenticated,
            ex.StatusCode
        );
    }

    /// <summary>
    /// Verifies that supplying an incorrect bearer token (one whose hash does not match the stored
    /// <see cref="RegisteredConnection.InboundApiKeyHash"/>) throws an <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/>.
    /// </summary>
    [TestMethod]
    public async Task InvalidToken_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", "Bearer wrong-token" },
            { "x-werkr-connection-id", _connectionId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                ctx,
                ( r, c ) => Task.FromResult( "ok" )
            )
        );

        Assert.AreEqual(
            StatusCode.Unauthenticated,
            ex.StatusCode
        );
    }

    /// <summary>
    /// Verifies that a valid token paired with a revoked <see cref="RegisteredConnection"/> (status set to
    /// <see cref="ConnectionStatus.Revoked"/>) throws an <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/>.
    /// </summary>
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
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                ctx,
                ( r, c ) => Task.FromResult( "ok" )
            )
        );

        Assert.AreEqual(
            StatusCode.Unauthenticated,
            ex.StatusCode
        );
    }

    /// <summary>
    /// Verifies that referencing a connection identifier that does not exist
    /// in the database throws an <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/>.
    /// </summary>
    [TestMethod]
    public async Task NonExistentConnection_ThrowsUnauthenticated( ) {
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
            { "x-werkr-connection-id", Guid.NewGuid( ).ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        RpcException ex = await Assert.ThrowsExactlyAsync<RpcException>( async ( ) =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                ctx,
                ( r, c ) => Task.FromResult( "ok" )
            )
        );

        Assert.AreEqual(
            StatusCode.Unauthenticated,
            ex.StatusCode
        );
    }

    /// <summary>
    /// Verifies that a successful authentication updates the
    /// <see cref="RegisteredConnection.LastSeen"/> timestamp on the
    /// <see cref="RegisteredConnection"/> row to a recent UTC time.
    /// </summary>
    [TestMethod]
    public async Task ValidToken_UpdatesLastSeen( ) {
        Metadata headers = CreateValidHeaders( );
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( r, c ) => Task.FromResult( "ok" )
        );

        // Verify LastSeen was set
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        RegisteredConnection? conn = await db.RegisteredConnections.FindAsync(
            [ _connectionId ],
            TestContext.CancellationToken
        );
        Assert.IsNotNull( conn!.LastSeen );
        Assert.IsGreaterThanOrEqualTo(
            DateTime.UtcNow.AddSeconds( -5 ),
            conn.LastSeen.Value
        );
    }

    /// <summary>
    /// Verifies that a valid request containing an "x-werkr-call-id" header
    /// stores the call identifier in the user-state dictionary under the
    /// "CallId" key.
    /// </summary>
    [TestMethod]
    public async Task CallId_StoredInUserState( ) {
        Guid callId = Guid.NewGuid( );
        Metadata headers = new( ) {
            { "authorization", $"Bearer {_rawToken}" },
            { "x-werkr-connection-id", _connectionId.ToString( ) },
            { "x-werkr-call-id", callId.ToString( ) },
        };
        TestServerCallContext ctx = TestServerCallContext.Create(
            headers,
            TestContext.CancellationToken
        );

        _ = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            ctx,
            ( r, c ) => Task.FromResult( "ok" )
        );

        Assert.IsTrue( ctx.ExposedUserState.ContainsKey( "CallId" ) );
        Assert.AreEqual(
            callId.ToString( ),
            ctx.ExposedUserState["CallId"]
        );
    }

    /// <summary>
    /// Creates a <see cref="Metadata"/> collection containing the minimum
    /// valid authorization and connection-id headers needed for a successful
    /// interceptor pass-through.
    /// </summary>
    private Metadata CreateValidHeaders( ) {
        return [
            new Metadata.Entry(
                "authorization",
                $"Bearer {_rawToken}"
            ),
            new Metadata.Entry(
                "x-werkr-connection-id",
                _connectionId.ToString( )
            ),
        ];
    }
}
