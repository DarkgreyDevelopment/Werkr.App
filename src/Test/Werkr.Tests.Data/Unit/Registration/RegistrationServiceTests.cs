using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Core.Registration;
using Werkr.Core.Registration.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Registration;

[TestClass]
public class RegistrationServiceTests {
    private static RSAKeyPair s_serverKeys = null!;
    private static RSAKeyPair s_agentKeys = null!;

    private SqliteConnection _connection = null!;
    private SqliteWerkrDbContext _dbContext = null!;
    private RegistrationService _service = null!;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void ClassInit( TestContext context ) {
        // Pre-generate RSA-4096 keys to avoid per-test overhead.
        s_serverKeys = EncryptionProvider.GenerateRSAKeyPair( );
        s_agentKeys = EncryptionProvider.GenerateRSAKeyPair( );
    }

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrDbContext> options = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _dbContext = new SqliteWerkrDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        _service = new RegistrationService(
            _dbContext,
            NullLogger<RegistrationService>.Instance,
            "https://server:5000" );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // -- GenerateBundleAsync --

    [TestMethod]
    public async Task GenerateBundleAsync_PersistsBundleToDatabase( ) {
        string encrypted = await _service.GenerateBundleAsync(
            "TestConn", "password123", TimeSpan.FromHours( 1 ), null, TestContext.CancellationToken );

        Assert.IsNotNull( encrypted );

        List<RegistrationBundle> bundles = await _dbContext.RegistrationBundles
            .ToListAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, bundles );
        Assert.AreEqual( "TestConn", bundles[0].ConnectionName );
        Assert.AreEqual( RegistrationStatus.Pending, bundles[0].Status );
    }

    // -- CompleteRegistrationAsync --

    [TestMethod]
    public async Task CompleteRegistrationAsync_ValidBundle_Succeeds( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.ApiKey );
        Assert.IsNotNull( result.SharedKey );
        Assert.IsNotNull( encryptedResponse );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_ExpiredBundle_ReturnsFailure( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        bundle.ExpiresAt = DateTime.UtcNow.AddHours( -1 );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_CompletedBundle_ReturnsFailure( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        bundle.Status = RegistrationStatus.Completed;
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_UnknownBundle_ReturnsFailure( ) {
        byte[] unknownBundleId = EncryptionProvider.GenerateRandomBytes( 16 );
        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( s_serverKeys.PublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            unknownBundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_EncryptedResponse_DecryptableByAgent( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( encryptedResponse );

        // Agent should be able to decrypt the response with its private key
        byte[] responseJson = EncryptionProvider.HybridDecrypt( encryptedResponse, s_agentKeys.PrivateKey );
        RegistrationResponsePayload? responsePayload = JsonSerializer.Deserialize<RegistrationResponsePayload>( responseJson );

        Assert.IsNotNull( responsePayload );
        Assert.AreEqual( result.ApiKey, responsePayload.AgentToServerApiKey );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_CreatesRegisteredConnection( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        _ = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        List<RegisteredConnection> connections = await _dbContext.RegisteredConnections
            .ToListAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, connections );
        Assert.AreEqual( "TestConn", connections[0].ConnectionName );
        Assert.AreEqual( "https://agent:5001", connections[0].RemoteUrl );
        Assert.AreEqual( ConnectionStatus.Connected, connections[0].Status );
        Assert.IsTrue( connections[0].IsServer );
    }

    [TestMethod]
    public async Task CompleteRegistrationAsync_StoresHashedApiKey( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, _) = await _service.CompleteRegistrationAsync(
            bundle.BundleId, encryptedAgentKey, "https://agent:5001", "TestAgent",
            TestContext.CancellationToken );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.ApiKey );

        RegisteredConnection connection = await _dbContext.RegisteredConnections
            .SingleAsync( TestContext.CancellationToken );

        // Server stores SHA-512 hash, not raw API key
        string expectedHash = EncryptionProvider.HashSHA512String( result.ApiKey );
        Assert.AreEqual( expectedHash, connection.InboundApiKeyHash );

        // Hash should be 128 hex chars (SHA-512 = 64 bytes)
        Assert.HasCount( 128, connection.InboundApiKeyHash );
    }

    // -- Helpers --

    private RegistrationBundle SeedPendingBundle( ) {
        RegistrationBundle bundle = new( ) {
            ConnectionName = "TestConn",
            ServerPublicKey = s_serverKeys.PublicKey,
            ServerPrivateKey = s_serverKeys.PrivateKey,
            BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
            Status = RegistrationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours( 1 ),
            KeySize = 4096,
        };
        _ = _dbContext.RegistrationBundles.Add( bundle );
        return bundle;
    }

    private static byte[] BuildEncryptedAgentPublicKey( RSAParameters serverPublicKey ) {
        byte[] agentPubKeyBytes = EncryptionProvider.SerializePublicKey( s_agentKeys.PublicKey );
        return EncryptionProvider.HybridEncrypt( agentPubKeyBytes, serverPublicKey );
    }
}
