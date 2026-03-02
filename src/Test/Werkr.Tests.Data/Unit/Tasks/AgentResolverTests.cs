using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Tasks;

[TestClass]
public class AgentResolverTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;
    private AgentConnectionManager _connectionManager = null!;
    private AgentResolver _resolver = null!;

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

        ServiceCollection services = new( );
        _ = services.AddDbContext<WerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped );
        _ = services.AddDbContext<SqliteWerkrDbContext>(
            b => b.UseSqlite( _connection ),
            ServiceLifetime.Scoped );
        _serviceProvider = services.BuildServiceProvider( );

        _connectionManager = new AgentConnectionManager(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance );

        _resolver = new AgentResolver( _dbContext, _connectionManager, NullLogger<AgentResolver>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _connectionManager?.Dispose( );
        _serviceProvider?.Dispose( );
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    private RegisteredConnection MakeConnection(
        string name, ConnectionStatus status, params string[] tags ) {
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

    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenNoAgents( ) {
        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"], TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenEmptyTags( ) {
        RegisteredConnection? result = await _resolver.ResolveAsync(
            [], TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task Resolve_ReturnsNull_WhenNoTagMatch( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent1", ConnectionStatus.Connected, "windows" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"], TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task Resolve_ReturnsMatch_WhenTagsIntersect( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent1", ConnectionStatus.Connected, "linux", "docker" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"], TestContext.CancellationToken );
        Assert.IsNotNull( result );
        Assert.AreEqual( "agent1", result.ConnectionName );
    }

    [TestMethod]
    public async Task Resolve_CaseInsensitive( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent1", ConnectionStatus.Connected, "Linux" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["LINUX"], TestContext.CancellationToken );
        Assert.IsNotNull( result );
    }

    [TestMethod]
    public async Task Resolve_IgnoresDisconnectedAgents( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "disconnected", ConnectionStatus.Disconnected, "linux" ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "revoked", ConnectionStatus.Revoked, "linux" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        RegisteredConnection? result = await _resolver.ResolveAsync(
            ["linux"], TestContext.CancellationToken );
        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task ResolveAll_ReturnsMultipleMatches( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent1", ConnectionStatus.Connected, "linux" ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent2", ConnectionStatus.Connected, "linux", "docker" ) );
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent3", ConnectionStatus.Connected, "windows" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        IReadOnlyList<RegisteredConnection> results = await _resolver.ResolveAllAsync(
            ["linux"], TestContext.CancellationToken );
        Assert.HasCount( 2, results );
    }

    [TestMethod]
    public async Task ResolveAll_EmptyTags_ReturnsEmpty( ) {
        _ = _dbContext.RegisteredConnections.Add( MakeConnection( "agent1", ConnectionStatus.Connected, "linux" ) );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        IReadOnlyList<RegisteredConnection> results = await _resolver.ResolveAllAsync(
            [], TestContext.CancellationToken );
        Assert.IsEmpty( results );
    }
}
