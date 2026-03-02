using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Server.Identity;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for <see cref="ApiKeyService"/>.
/// Uses an in-memory EF Core database.
/// </summary>
[TestClass]
public class ApiKeyServiceTests {
    public TestContext TestContext { get; set; } = null!;

    private WerkrIdentityDbContext _dbContext = null!;
    private ApiKeyService _service = null!;

    [TestInitialize]
    public void TestInit( ) {
        DbContextOptions<WerkrIdentityDbContext> options = new DbContextOptionsBuilder<WerkrIdentityDbContext>( )
            .UseInMemoryDatabase( $"ApiKeyTests_{Guid.NewGuid( )}" )
            .Options;
        _dbContext = new WerkrIdentityDbContext( options );
        ILogger<ApiKeyService> logger = NullLogger<ApiKeyService>.Instance;
        _service = new ApiKeyService( _dbContext, logger );
    }

    [TestCleanup]
    public void TestCleanup( ) => _dbContext.Dispose( );

    [TestMethod]
    public async Task CreateAsync_ReturnsRawKeyWithPrefix( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Test Key", "Admin", "user-1", ct: TestContext.CancellationToken );

        Assert.IsNotNull( rawKey );
        Assert.StartsWith( "wk_", rawKey, "Raw key should start with 'wk_' prefix." );
        Assert.AreEqual( "Test Key", entity.Name );
        Assert.AreEqual( "Admin", entity.Role );
        Assert.AreEqual( "user-1", entity.CreatedByUserId );
        Assert.IsFalse( entity.IsRevoked );
        Assert.IsNotNull( entity.KeyHash );
        Assert.AreNotEqual( string.Empty, entity.KeyHash );
    }

    [TestMethod]
    public async Task CreateAsync_StoresKeyPrefixFromRawKey( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Prefix Key", "Operator", "user-1", ct: TestContext.CancellationToken );

        string expectedPrefix = rawKey[..Math.Min( 12, rawKey.Length )];
        Assert.AreEqual( expectedPrefix, entity.KeyPrefix );
    }

    [TestMethod]
    public async Task ValidateAsync_ValidKey_ReturnsEntity( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Valid Key", "Admin", "user-1", ct: TestContext.CancellationToken );

        ApiKey? result = await _service.ValidateAsync( rawKey, TestContext.CancellationToken );

        Assert.IsNotNull( result );
        Assert.AreEqual( entity.Id, result.Id );
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidKey_ReturnsNull( ) {
        ApiKey? result = await _service.ValidateAsync(
            "wk_invalid_key_1234", TestContext.CancellationToken );

        Assert.IsNull( result );
    }

    [TestMethod]
    public async Task ValidateAsync_RevokedKey_ReturnsNull( ) {
        (ApiKey _, string rawKey) = await _service.CreateAsync(
            "Revoked Key", "Admin", "user-1", ct: TestContext.CancellationToken );

        // Validate once (should succeed)
        Assert.IsNotNull( await _service.ValidateAsync( rawKey, TestContext.CancellationToken ) );

        // Revoke
        ApiKey revokedEntity = _dbContext.ApiKeys.First( );
        revokedEntity.IsRevoked = true;
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        // Validate again (should fail)
        Assert.IsNull( await _service.ValidateAsync( rawKey, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task ValidateAsync_ExpiredKey_ReturnsNull( ) {
        (ApiKey _, string rawKey) = await _service.CreateAsync(
            "Expired Key", "Admin", "user-1",
            expiresUtc: DateTime.UtcNow.AddMinutes( -5 ),
            ct: TestContext.CancellationToken );

        Assert.IsNull( await _service.ValidateAsync( rawKey, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task ValidateAsync_UpdatesLastUsedUtc( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Used Key", "Admin", "user-1", ct: TestContext.CancellationToken );

        Assert.IsNull( entity.LastUsedUtc );

        _ = await _service.ValidateAsync( rawKey, TestContext.CancellationToken );

        ApiKey? updated = await _dbContext.ApiKeys.FindAsync( [entity.Id], TestContext.CancellationToken );
        Assert.IsNotNull( updated?.LastUsedUtc );
    }

    [TestMethod]
    public async Task RevokeAsync_ExistingKey_ReturnsTrue( ) {
        (ApiKey entity, string _) = await _service.CreateAsync(
            "To Revoke", "Admin", "user-1", ct: TestContext.CancellationToken );

        bool result = await _service.RevokeAsync( entity.Id, TestContext.CancellationToken );

        Assert.IsTrue( result );

        ApiKey? revoked = await _dbContext.ApiKeys.FindAsync( [entity.Id], TestContext.CancellationToken );
        Assert.IsTrue( revoked?.IsRevoked );
    }

    [TestMethod]
    public async Task RevokeAsync_NonExistentKey_ReturnsFalse( ) {
        bool result = await _service.RevokeAsync( Guid.NewGuid( ), TestContext.CancellationToken );
        Assert.IsFalse( result );
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsCreatedKeys( ) {
        _ = await _service.CreateAsync( "Key1", "Admin", "user-1", ct: TestContext.CancellationToken );
        _ = await _service.CreateAsync( "Key2", "Operator", "user-2", ct: TestContext.CancellationToken );

        IReadOnlyList<ApiKey> keys = await _service.GetAllAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, keys );
    }

    [TestMethod]
    public async Task GetByUserAsync_FiltersCorrectly( ) {
        _ = await _service.CreateAsync( "User1 Key", "Admin", "user-1", ct: TestContext.CancellationToken );
        _ = await _service.CreateAsync( "User2 Key", "Operator", "user-2", ct: TestContext.CancellationToken );

        IReadOnlyList<ApiKey> user1Keys = await _service.GetByUserAsync( "user-1", TestContext.CancellationToken );
        IReadOnlyList<ApiKey> user2Keys = await _service.GetByUserAsync( "user-2", TestContext.CancellationToken );

        Assert.HasCount( 1, user1Keys );
        Assert.AreEqual( "User1 Key", user1Keys[0].Name );
        Assert.HasCount( 1, user2Keys );
        Assert.AreEqual( "User2 Key", user2Keys[0].Name );
    }

    [TestMethod]
    public async Task CreateAsync_GeneratesUniqueKeys( ) {
        (_, string rawKey1) = await _service.CreateAsync( "Key A", "Admin", "user-1", ct: TestContext.CancellationToken );
        (_, string rawKey2) = await _service.CreateAsync( "Key B", "Admin", "user-1", ct: TestContext.CancellationToken );

        Assert.AreNotEqual( rawKey1, rawKey2 );
    }

    [TestMethod]
    public async Task ValidateAsync_NullOrEmpty_ReturnsNull( ) {
        Assert.IsNull( await _service.ValidateAsync( null!, TestContext.CancellationToken ) );
        Assert.IsNull( await _service.ValidateAsync( "", TestContext.CancellationToken ) );
        Assert.IsNull( await _service.ValidateAsync( "  ", TestContext.CancellationToken ) );
    }
}
