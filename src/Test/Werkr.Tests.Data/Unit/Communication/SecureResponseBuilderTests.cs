using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Communication;

/// <summary>
/// Unit tests for <see cref="SecureResponseBuilder"/>. Validates that response
/// metadata is populated correctly based on the notification outbox state.
/// </summary>
[TestClass]
public class SecureResponseBuilderTests {
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;
    private SecureResponseBuilder _builder = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        string dbName = $"srb_{Guid.NewGuid( ):N}";
        string connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared";

        _connection = new SqliteConnection( connectionString );
        _connection.Open( );

        ServiceCollection services = new( );
        _ = services.AddDbContext<SqliteWerkrDbContext>( opt => opt.UseSqlite( connectionString ) );
        _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<SqliteWerkrDbContext>( ) );
        _serviceProvider = services.BuildServiceProvider( );

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.Database.EnsureCreated( );

        IServiceScopeFactory scopeFactory =
            _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        _builder = new SecureResponseBuilder( scopeFactory );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider?.Dispose( );
        _connection?.Dispose( );
    }

    private RegisteredConnection SeedAgent( ) {
        byte[] key = new byte[32];
        Random.Shared.NextBytes( key );
        RegisteredConnection agent = new( ) {
            Id = Guid.NewGuid( ),
            ConnectionName = "test-agent",
            RemoteUrl = "https://localhost:5100",
            IsServer = true,
            Status = ConnectionStatus.Connected,
            SharedKey = key,
            ActiveKeyId = "key-1",
            InboundApiKeyHash = "hash",
            OutboundApiKey = "key",
            Tags = [],
            AgentVersion = "1.0.0",
        };

        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.RegisteredConnections.Add( agent );
        _ = db.SaveChanges( );
        return agent;
    }

    private void SeedNotification( Guid connectionId ) {
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.PendingAgentNotifications.Add( new PendingAgentNotification {
            ConnectionId = connectionId,
            Channel = "config_update",
            CreatedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddHours( 1 ),
        } );
        _ = db.SaveChanges( );
    }

    /// <summary>
    /// Verifies that <see cref="SecureResponseBuilder.EncryptResponseAsync{T}"/>
    /// sets <see cref="ResponseMetadata.UrgentCommandsPending"/> to true when the
    /// notification outbox has rows for the agent.
    /// </summary>
    [TestMethod]
    public async Task EncryptResponseAsync_SetsUrgentTrue_WhenOutboxNonEmpty( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );
        SeedNotification( agent.Id );

        AgentHeartbeatResponse response = new( ) { Acknowledged = true, ServerVersion = "1.0" };

        EncryptedEnvelope envelope = await _builder.EncryptResponseAsync(
            response, agent, ct );

        Assert.IsNotNull( envelope );
        Assert.IsTrue( response.Metadata?.UrgentCommandsPending,
            "UrgentCommandsPending should be true when outbox has notifications." );
    }

    /// <summary>
    /// Verifies that <see cref="SecureResponseBuilder.EncryptResponseAsync{T}"/>
    /// sets <see cref="ResponseMetadata.UrgentCommandsPending"/> to false when the
    /// notification outbox is empty for the agent.
    /// </summary>
    [TestMethod]
    public async Task EncryptResponseAsync_SetsUrgentFalse_WhenOutboxEmpty( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );
        // No notifications seeded

        AgentHeartbeatResponse response = new( ) { Acknowledged = true, ServerVersion = "1.0" };

        EncryptedEnvelope envelope = await _builder.EncryptResponseAsync(
            response, agent, ct );

        Assert.IsNotNull( envelope );
        Assert.IsFalse( response.Metadata?.UrgentCommandsPending,
            "UrgentCommandsPending should be false when outbox is empty." );
    }

    /// <summary>
    /// Verifies that the encrypted envelope can be round-tripped (encrypted then
    /// decrypted) with the agent's shared key.
    /// </summary>
    [TestMethod]
    public async Task EncryptResponseAsync_ProducesDecryptableEnvelope( ) {
        CancellationToken ct = TestContext.CancellationToken;
        RegisteredConnection agent = SeedAgent( );

        AgentHeartbeatResponse response = new( ) {
            Acknowledged = true,
            ServerVersion = "2.3.0",
        };

        EncryptedEnvelope envelope = await _builder.EncryptResponseAsync(
            response, agent, ct );

        AgentHeartbeatResponse decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatResponse>(
            envelope, agent.SharedKey );

        Assert.IsTrue( decrypted.Acknowledged );
        Assert.AreEqual( "2.3.0", decrypted.ServerVersion );
    }

    /// <summary>
    /// Verifies that the static <see cref="SecureResponseBuilder.EncryptResponse{T}"/>
    /// (no metadata, for registration path) produces a valid encrypted envelope.
    /// </summary>
    [TestMethod]
    public void EncryptResponse_Static_ProducesValidEnvelope( ) {
        byte[] key = new byte[32];
        Random.Shared.NextBytes( key );

        AgentHeartbeatResponse response = new( ) {
            Acknowledged = true,
            ServerVersion = "1.0.0",
        };

        EncryptedEnvelope envelope = SecureResponseBuilder.EncryptResponse(
            response, key, "registration" );

        Assert.IsNotNull( envelope );

        AgentHeartbeatResponse decrypted = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatResponse>(
            envelope, key );
        Assert.IsTrue( decrypted.Acknowledged );
    }
}
