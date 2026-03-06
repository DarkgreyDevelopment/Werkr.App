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

/// <summary>
/// Contains unit tests for the <see cref="CommandDispatcher"/> class defined in Werkr.Core. Validates that unsupported
/// <see cref="OperatorType"/> values produce appropriate error outputs for both command and script execution paths.
/// </summary>
[TestClass]
public class CommandDispatcherTests {
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
    /// The <see cref="AgentConnectionManager"/> managing gRPC channels for test dispatching.
    /// </summary>
    private AgentConnectionManager _connectionManager = null!;
    /// <summary>
    /// The <see cref="CommandDispatcher"/> instance under test.
    /// </summary>
    private CommandDispatcher _dispatcher = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database, registers required services, and constructs the <see
    /// cref="CommandDispatcher"/> under test.
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

        _connectionManager = new AgentConnectionManager(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( ),
            NullLogger<AgentConnectionManager>.Instance
        );

        _dispatcher = new CommandDispatcher(
            _connectionManager,
            NullLogger<CommandDispatcher>.Instance
        );
    }

    /// <summary>
    /// Disposes the connection manager, service provider, database context, and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _connectionManager.Dispose( );
        _serviceProvider.Dispose( );
        _dbContext.Dispose( );
        _connection.Dispose( );
    }

    /// <summary>
    /// Verifies that <see cref="ExecuteCommandAsync"/> returns a single error output when an unsupported <see
    /// cref="OperatorType"/> is provided.
    /// </summary>
    [TestMethod]
    public async Task ExecuteCommandAsync_UnsupportedOperator_ReturnsSingleError( ) {
        RegisteredConnection conn = SeedServerConnection( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        List<OperatorOutput> outputs = await ToListAsync(
            _dispatcher.ExecuteCommandAsync(
                conn.Id,
                OperatorType.Action,
                "noop",
                TestContext.CancellationToken
            ),
            TestContext.CancellationToken
        );

        Assert.HasCount(
            1,
            outputs
        );
        Assert.AreEqual(
            "Error",
            outputs[0].LogLevel
        );
        Assert.Contains(
            "Unsupported operator type",
            outputs[0].Message
        );
    }

    /// <summary>
    /// Verifies that <see cref="ExecuteScriptAsync"/> returns a single error output when an unsupported <see
    /// cref="OperatorType"/> is provided and no script arguments are supplied.
    /// </summary>
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
                TestContext.CancellationToken
            ),
            TestContext.CancellationToken
        );

        Assert.HasCount(
            1,
            outputs
        );
        Assert.AreEqual(
            "Error",
            outputs[0].LogLevel
        );
        Assert.Contains(
            "Unsupported operator type",
            outputs[0].Message
        );
    }

    /// <summary>
    /// Verifies that <see cref="ExecuteScriptAsync"/> returns a single error output when an unsupported <see
    /// cref="OperatorType"/> is provided even with script arguments.
    /// </summary>
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
                TestContext.CancellationToken
            ),
            TestContext.CancellationToken
        );

        Assert.HasCount(
            1,
            outputs
        );
        Assert.AreEqual(
            "Error",
            outputs[0].LogLevel
        );
        Assert.Contains(
            "Unsupported operator type",
            outputs[0].Message
        );
    }

    /// <summary>
    /// Creates and persists a <see cref="RegisteredConnection"/> with <see cref="ConnectionStatus.Connected"/> status
    /// and generated RSA keys.
    /// </summary>
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

    /// <summary>
    /// Collects all elements from an <see cref="IAsyncEnumerable{T}"/> of <see cref="OperatorOutput"/> into a list.
    /// </summary>
    private static async Task<List<OperatorOutput>> ToListAsync(
        IAsyncEnumerable<OperatorOutput> sequence,
        CancellationToken cancellationToken
    ) {

        List<OperatorOutput> outputs = [];
        await foreach (OperatorOutput output in sequence.WithCancellation( cancellationToken )) {
            outputs.Add( output );
        }

        return outputs;
    }
}
