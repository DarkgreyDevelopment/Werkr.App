using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Tasks;

/// <summary>
/// Contains unit tests for the <see cref="AgentResolver"/> class defined in Werkr.Core. Validates agent resolution by
/// tag matching, case insensitivity, disconnected-agent filtering, multi-agent resolution, and empty-tag handling using
/// an in-memory SQLite database.
/// </summary>
[TestClass]
public class AgentResolverTests {
    /// <summary>
    /// The in-memory SQLite connection kept open for the duration of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The <see cref="SqliteWerkrDbContext"/> used for seeding and querying test data.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="AgentResolver"/> instance under test.
    /// </summary>
    private AgentResolver _resolver = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database, registers services, and constructs the <see cref="AgentResolver"/> under
    /// test.
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

        _resolver = new AgentResolver(
            _dbContext,
            NullLogger<AgentResolver>.Instance
        );
    }

    /// <summary>
    /// Disposes the connection manager, service provider, database context, and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Creates a <see cref="RegisteredConnection"/> with the specified name, status, and tags for test seeding.
    /// </summary>
    private static RegisteredConnection MakeConnection(
        string name,
        ConnectionStatus status,
        params string[] tags
    ) {
        return new RegisteredConnection {
            ConnectionName = name,
            RemoteUrl = $"https://{name}.test:5100",
            Status = status,
            Tags = tags,
            IsServer = true,
            SharedKey = new byte[32],
            LocalPublicKey = System.Security.Cryptography.RSA.Create( 2048 ).ExportParameters( false ),
            RemotePublicKey = System.Security.Cryptography.RSA.Create( 2048 ).ExportParameters( false ),
        };
    }

    /// <summary>
    /// Verifies that resolving returns <see langword="null"/> when no agents exist in the database.
    /// </summary>
    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenNoAgents( ) {
        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"],
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that resolving with empty tags returns <see langword="null"/>.
    /// </summary>
    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenEmptyTags( ) {
        RegisteredConnection? result = await _resolver.ResolveAsync(
            [],
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that resolving returns <see langword="null"/> when no agent's tags match the request.
    /// </summary>
    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenNoTagMatch( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent1",
            ConnectionStatus.Connected,
            "windows"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"],
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that resolving returns a matching agent when tags intersect.
    /// </summary>
    [TestMethod]
    public async Task Resolve_ReturnsMatch_WhenTagsIntersect( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent1",
            ConnectionStatus.Connected,
            "linux",
            "docker"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"],
            TestContext.CancellationToken
        );
        Assert.IsNotNull( result );
        Assert.AreEqual(
            "agent1",
            result.ConnectionName
        );
    }

    /// <summary>
    /// Verifies that tag resolution is case-insensitive.
    /// </summary>
    [TestMethod]
    public async Task Resolve_CaseInsensitive( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent1",
            ConnectionStatus.Connected,
            "Linux"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["LINUX"],
            TestContext.CancellationToken
        );
        Assert.IsNotNull( result );
    }

    /// <summary>
    /// Verifies that disconnected and revoked agents are excluded from resolution.
    /// </summary>
    [TestMethod]
    public async Task Resolve_IgnoresDisconnectedAgents( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "disconnected",
            ConnectionStatus.Disconnected,
            "linux"
        ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "revoked",
            ConnectionStatus.Revoked,
            "linux"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"],
            TestContext.CancellationToken
        );
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="ResolveAllAsync"/> returns all matching connected agents.
    /// </summary>
    [TestMethod]
    public async Task ResolveAll_ReturnsMultipleMatches( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent1",
            ConnectionStatus.Connected,
            "linux"
        ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent2",
            ConnectionStatus.Connected,
            "linux",
            "docker"
        ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent3",
            ConnectionStatus.Connected,
            "windows"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        IReadOnlyList<RegisteredConnection> results = await _resolver.ResolveAllAsync(
            ["linux"],
            TestContext.CancellationToken
        );
        Assert.HasCount(
            2,
            results
        );
    }

    /// <summary>
    /// Verifies that <see cref="ResolveAllAsync"/> returns an empty list when empty tags are provided.
    /// </summary>
    [TestMethod]
    public async Task ResolveAll_EmptyTags_ReturnsEmpty( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection(
            "agent1",
            ConnectionStatus.Connected,
            "linux"
        ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        IReadOnlyList<RegisteredConnection> results = await _resolver.ResolveAllAsync(
            [],
            TestContext.CancellationToken
        );
        Assert.IsEmpty( results );
    }
}
