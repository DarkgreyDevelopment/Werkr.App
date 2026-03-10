using System.Security.Cryptography;
using System.Text.Json;
using Werkr.Core.Cryptography;
using Werkr.Core.Cryptography.KeyInfo;
using Werkr.Core.Registration;
using Werkr.Core.Registration.Models;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Registration;

/// <summary>
/// Contains unit tests for the <see cref="RegistrationService"/> class defined in Werkr.Core. Validates bundle
/// generation, registration completion, expired/completed/unknown bundle handling, encrypted response decryption,
/// connection creation, and API key hashing.
/// </summary>
[TestClass]
public class RegistrationServiceTests {
    /// <summary>
    /// The pre-generated server RSA key pair used across all tests.
    /// </summary>
    private static RSAKeyPair s_serverKeys = null!;
    /// <summary>
    /// The pre-generated agent RSA key pair used across all tests.
    /// </summary>
    private static RSAKeyPair s_agentKeys = null!;

    /// <summary>
    /// The in-memory SQLite connection kept open for the duration of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The <see cref="SqliteWerkrDbContext"/> used for seeding and querying test data.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="RegistrationService"/> instance under test.
    /// </summary>
    private RegistrationService _service = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Generates server and agent RSA key pairs once for all tests.
    /// </summary>
    [ClassInitialize]
    public static void ClassInit( TestContext context ) {
        // Pre-generate RSA-4096 keys to avoid per-test overhead.
        s_serverKeys = EncryptionProvider.GenerateRSAKeyPair( );
        s_agentKeys = EncryptionProvider.GenerateRSAKeyPair( );
    }

    /// <summary>
    /// Creates an in-memory SQLite database and constructs the <see cref="RegistrationService"/> under test.
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

        _service = new RegistrationService(
            _dbContext,
            NullLogger<RegistrationService>.Instance,
            "https://server:5000"
        );
    }

    /// <summary>
    /// Disposes the database context and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    // -- GenerateBundleAsync --

    /// <summary>
    /// Verifies that <see cref="GenerateBundleAsync"/> persists a registration bundle to the database with the expected
    /// connection name and pending status.
    /// </summary>
    [TestMethod]
    public async Task GenerateBundleAsync_PersistsBundleToDatabase( ) {
        string encrypted = await _service.GenerateBundleAsync(
            "TestConn",
            "password123",
            TimeSpan.FromHours( 1 ),
            null,
            TestContext.CancellationToken
        );

        Assert.IsNotNull( encrypted );

        List<RegistrationBundle> bundles = await _dbContext.RegistrationBundles
            .ToListAsync( TestContext.CancellationToken );

        Assert.HasCount(
            1,
            bundles
        );
        Assert.AreEqual(
            "TestConn",
            bundles[0].ConnectionName
        );
        Assert.AreEqual(
            RegistrationStatus.Pending,
            bundles[0].Status
        );
    }

    // -- CompleteRegistrationAsync --

    /// <summary>
    /// Verifies that <see cref="CompleteRegistrationAsync"/> succeeds for a valid pending bundle and returns a success
    /// result with API key, shared key, and encrypted response.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_ValidBundle_Succeeds( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.ApiKey );
        Assert.IsNotNull( result.SharedKey );
        Assert.IsNotNull( encryptedResponse );
    }

    /// <summary>
    /// Verifies that <see cref="CompleteRegistrationAsync"/> returns failure for an expired bundle.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_ExpiredBundle_ReturnsFailure( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        bundle.ExpiresAt = DateTime.UtcNow.AddHours( -1 );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    /// <summary>
    /// Verifies that <see cref="CompleteRegistrationAsync"/> returns failure for a bundle that is already completed.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_CompletedBundle_ReturnsFailure( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        bundle.Status = RegistrationStatus.Completed;
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    /// <summary>
    /// Verifies that <see cref="CompleteRegistrationAsync"/> returns failure for an unknown bundle ID.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_UnknownBundle_ReturnsFailure( ) {
        byte[] unknownBundleId = EncryptionProvider.GenerateRandomBytes( 16 );
        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( s_serverKeys.PublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            unknownBundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsFalse( result.Success );
        Assert.IsNull( encryptedResponse );
    }

    /// <summary>
    /// Verifies that the encrypted response from <see cref="CompleteRegistrationAsync"/> can be decrypted by the agent
    /// using its private key and matches the returned API key.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_EncryptedResponse_DecryptableByAgent( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, byte[]? encryptedResponse) = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( encryptedResponse );

        // Agent should be able to decrypt the response with its private key
        byte[] responseJson = EncryptionProvider.HybridDecrypt(
            encryptedResponse,
            s_agentKeys.PrivateKey
        );
        RegistrationResponsePayload? responsePayload = JsonSerializer.Deserialize<RegistrationResponsePayload>( responseJson );

        Assert.IsNotNull( responsePayload );
        Assert.AreEqual(
            result.ApiKey,
            responsePayload.AgentToServerApiKey
        );
    }

    /// <summary>
    /// Verifies that <see cref="CompleteRegistrationAsync"/> creates a <see cref="RegisteredConnection"/> entity with
    /// the correct name, URL, status, and server flag.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_CreatesRegisteredConnection( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        _ = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        List<RegisteredConnection> connections = await _dbContext.RegisteredConnections
            .ToListAsync( TestContext.CancellationToken );

        Assert.HasCount(
            1,
            connections
        );
        Assert.AreEqual(
            "TestConn",
            connections[0].ConnectionName
        );
        Assert.AreEqual(
            "https://agent:5001",
            connections[0].RemoteUrl
        );
        Assert.AreEqual(
            ConnectionStatus.Connected,
            connections[0].Status
        );
        Assert.IsTrue( connections[0].IsServer );
    }

    /// <summary>
    /// Verifies that the inbound API key hash stored in the registered connection matches the SHA-512 hash of the
    /// generated API key.
    /// </summary>
    [TestMethod]
    public async Task CompleteRegistrationAsync_StoresHashedApiKey( ) {
        RegistrationBundle bundle = SeedPendingBundle( );
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        byte[] encryptedAgentKey = BuildEncryptedAgentPublicKey( bundle.ServerPublicKey );

        (AgentRegistrationResult result, _) = await _service.CompleteRegistrationAsync(
            bundle.BundleId,
            encryptedAgentKey,
            "https://agent:5001",
            "TestAgent",
            TestContext.CancellationToken
        );

        Assert.IsTrue( result.Success );
        Assert.IsNotNull( result.ApiKey );

        RegisteredConnection connection = await _dbContext.RegisteredConnections
            .SingleAsync( TestContext.CancellationToken );

        // Server stores SHA-512 hash, not raw API key
        string expectedHash = EncryptionProvider.HashSHA512String( result.ApiKey );
        Assert.AreEqual(
            expectedHash,
            connection.InboundApiKeyHash
        );

        // Hash should be 128 hex chars (SHA-512 = 64 bytes)
        Assert.HasCount(
            128,
            connection.InboundApiKeyHash
        );
    }

    // -- Helpers --

    /// <summary>
    /// Seeds a pending <see cref="RegistrationBundle"/> with generated server keys and a one-hour expiration.
    /// </summary>
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

    /// <summary>
    /// Encrypts the agent's public key with the server's RSA public key using hybrid encryption.
    /// </summary>
    private static byte[] BuildEncryptedAgentPublicKey( RSAParameters serverPublicKey ) {
        byte[] agentPubKeyBytes = EncryptionProvider.SerializePublicKey( s_agentKeys.PublicKey );
        return EncryptionProvider.HybridEncrypt(
            agentPubKeyBytes,
            serverPublicKey
        );
    }
}
