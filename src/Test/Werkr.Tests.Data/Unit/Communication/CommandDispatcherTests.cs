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
public class CommandDispatcherTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private ServiceProvider _serviceProvider = null!;
    private AgentConnectionManager _connectionManager = null!;
    private CommandDispatcher _dispatcher = null!;

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

        _dispatcher = new CommandDispatcher(
            _connectionManager,
            NullLogger<CommandDispatcher>.Instance );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _connectionManager.Dispose( );
        _serviceProvider.Dispose( );
        _dbContext.Dispose( );
        _connection.Dispose( );
    }

    [TestMethod]
    public async Task ExecuteCommandAsync_UnsupportedOperator_ReturnsSingleError( ) {
        RegisteredConnection conn = SeedServerConnection( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        List<OperatorOutput> outputs = await ToListAsync(
            _dispatcher.ExecuteCommandAsync(
                conn.Id,
                OperatorType.Action,
                "noop",
                TestContext.CancellationToken ),
            TestContext.CancellationToken );

        Assert.HasCount( 1, outputs );
        Assert.AreEqual( "Error", outputs[0].LogLevel );
        Assert.Contains( "Unsupported operator type", outputs[0].Message );
    }

    [TestMethod]
    public async Task ExecuteScriptAsync_UnsupportedOperatorWithoutArgs_ReturnsSingleError( ) {
        RegisteredConnection conn = SeedServerConnection( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        List<OperatorOutput> outputs = await ToListAsync(
            _dispatcher.ExecuteScriptAsync(
                conn.Id,
                OperatorType.Action,
                "script.ps1",
                args: null,
                TestContext.CancellationToken ),
            TestContext.CancellationToken );

        Assert.HasCount( 1, outputs );
        Assert.AreEqual( "Error", outputs[0].LogLevel );
        Assert.Contains( "Unsupported operator type", outputs[0].Message );
    }

    [TestMethod]
    public async Task ExecuteScriptAsync_UnsupportedOperatorWithArgs_ReturnsSingleError( ) {
        RegisteredConnection conn = SeedServerConnection( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        List<OperatorOutput> outputs = await ToListAsync(
            _dispatcher.ExecuteScriptAsync(
                conn.Id,
                OperatorType.Action,
                "script.ps1",
                ["arg1", "arg2"],
                TestContext.CancellationToken ),
            TestContext.CancellationToken );

        Assert.HasCount( 1, outputs );
        Assert.AreEqual( "Error", outputs[0].LogLevel );
        Assert.Contains( "Unsupported operator type", outputs[0].Message );
    }

    private RegisteredConnection SeedServerConnection( ) {
        RSAKeyPair keys = EncryptionProvider.GenerateRSAKeyPair( );

        RegisteredConnection conn = new( ) {
            ConnectionName = "DispatcherAgent",
            RemoteUrl = "https://localhost:54321",
            LocalPublicKey = keys.PublicKey,
            LocalPrivateKey = keys.PrivateKey,
            RemotePublicKey = keys.PublicKey,
            OutboundApiKey = "outbound-key",
            InboundApiKeyHash = "inbound-hash",
            SharedKey = EncryptionProvider.GenerateRandomBytes( 32 ),
            IsServer = true,
            Status = ConnectionStatus.Connected,
        };

        _ = _dbContext.RegisteredConnections.Add( conn );
        return conn;
    }

    private static async Task<List<OperatorOutput>> ToListAsync(
        IAsyncEnumerable<OperatorOutput> sequence,
        CancellationToken cancellationToken ) {

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in sequence.WithCancellation( cancellationToken )) {
            outputs.Add( output );
        }

        return outputs;
    }
}
