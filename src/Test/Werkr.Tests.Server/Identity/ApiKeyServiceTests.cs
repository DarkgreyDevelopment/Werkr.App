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
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> used for
    /// cancellation token access and test run metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The in-memory <see cref="WerkrIdentityDbContext"/> instance used for
    /// database operations during each test.
    /// </summary>
    private WerkrIdentityDbContext _dbContext = null!;
    /// <summary>
    /// The <see cref="ApiKeyService"/> instance under test, configured with
    /// the in-memory database context.
    /// </summary>
    private ApiKeyService _service = null!;

    /// <summary>
    /// Initializes a fresh in-memory database and
    /// <see cref="ApiKeyService"/> instance before each test to ensure
    /// complete isolation between test methods.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        DbContextOptions<WerkrIdentityDbContext> options =
            new DbContextOptionsBuilder<WerkrIdentityDbContext>( )
                .UseInMemoryDatabase( $"ApiKeyTests_{Guid.NewGuid( )}" )
                .Options;
        _dbContext = new WerkrIdentityDbContext( options );
        ILogger<ApiKeyService> logger = NullLogger<ApiKeyService>.Instance;
        _service = new ApiKeyService(
            _dbContext,
            logger
        );
    }

    /// <summary>
    /// Disposes the in-memory database context after each test to
    /// release resources.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) => _dbContext.Dispose( );

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.CreateAsync"/> returns a raw
    /// key string that starts with the "wk_" prefix and that the created
    /// <see cref="ApiKey"/> entity has the correct name, role, creator
    /// user ID, non-revoked status, and a non-empty hash value.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_ReturnsRawKeyWithPrefix( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Test Key",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        Assert.IsNotNull( rawKey );
        Assert.StartsWith(
            "wk_",
            rawKey,
            "Raw key should start with 'wk_' prefix."
        );
        Assert.AreEqual( "Test Key", entity.Name );
        Assert.AreEqual( "Admin", entity.Role );
        Assert.AreEqual( "user-1", entity.CreatedByUserId );
        Assert.IsFalse( entity.IsRevoked );
        Assert.IsNotNull( entity.KeyHash );
        Assert.AreNotEqual( string.Empty, entity.KeyHash );
    }

    /// <summary>
    /// Verifies that the <see cref="ApiKey.KeyPrefix"/> stored on the
    /// entity matches the first twelve characters (or fewer if the key is
    /// shorter) of the raw key returned by <see cref="CreateAsync"/>.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_StoresKeyPrefixFromRawKey( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Prefix Key",
            "Operator",
            "user-1",
            ct: TestContext.CancellationToken
        );

        string expectedPrefix = rawKey[..Math.Min( 12, rawKey.Length )];
        Assert.AreEqual( expectedPrefix, entity.KeyPrefix );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.ValidateAsync"/> returns the
    /// correct <see cref="ApiKey"/> entity when given a valid, non-revoked,
    /// non-expired raw key string.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_ValidKey_ReturnsEntity( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Valid Key",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        ApiKey? result = await _service.ValidateAsync(
            rawKey,
            TestContext.CancellationToken
        );

        Assert.IsNotNull( result );
        Assert.AreEqual( entity.Id, result.Id );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.ValidateAsync"/> returns
    /// <see langword="null"/> when given a raw key string that does not
    /// match any stored API key hash in the database.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_InvalidKey_ReturnsNull( ) {
        ApiKey? result = await _service.ValidateAsync(
            "wk_invalid_key_1234",
            TestContext.CancellationToken
        );

        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.ValidateAsync"/> returns
    /// <see langword="null"/> for an API key that has been revoked (i.e.,
    /// its <see cref="IsRevoked"/> flag is set to <see langword="true"/>),
    /// even though the raw key string is valid.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_RevokedKey_ReturnsNull( ) {
        (ApiKey _, string rawKey) = await _service.CreateAsync(
            "Revoked Key",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        // Validate once (should succeed)
        Assert.IsNotNull(
            await _service.ValidateAsync(
                rawKey,
                TestContext.CancellationToken
            )
        );

        // Revoke
        ApiKey revokedEntity = _dbContext.ApiKeys.First( );
        revokedEntity.IsRevoked = true;
        _ = await _dbContext.SaveChangesAsync( TestContext.CancellationToken );

        // Validate again (should fail)
        Assert.IsNull(
            await _service.ValidateAsync(
                rawKey,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.ValidateAsync"/> returns
    /// <see langword="null"/> for an API key whose <see cref="ExpiresUtc"/>
    /// is in the past, confirming that expired keys are rejected.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_ExpiredKey_ReturnsNull( ) {
        (ApiKey _, string rawKey) = await _service.CreateAsync(
            "Expired Key",
            "Admin",
            "user-1",
            expiresUtc: DateTime.UtcNow.AddMinutes( -5 ),
            ct: TestContext.CancellationToken
        );

        Assert.IsNull(
            await _service.ValidateAsync(
                rawKey,
                TestContext.CancellationToken
            )
        );
    }

    /// <summary>
    /// Verifies that a successful call to
    /// <see cref="ApiKeyService.ValidateAsync"/> updates the
    /// <see cref="LastUsedUtc"/> timestamp on the <see cref="ApiKey"/>
    /// entity, enabling usage tracking.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_UpdatesLastUsedUtc( ) {
        (ApiKey entity, string rawKey) = await _service.CreateAsync(
            "Used Key",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        Assert.IsNull( entity.LastUsedUtc );

        _ = await _service.ValidateAsync(
            rawKey,
            TestContext.CancellationToken
        );

        ApiKey? updated = await _dbContext.ApiKeys.FindAsync(
            [entity.Id],
            TestContext.CancellationToken
        );
        Assert.IsNotNull( updated?.LastUsedUtc );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.RevokeAsync"/> returns
    /// <see langword="true"/> and sets the <see cref="IsRevoked"/> flag on
    /// an existing API key entity to <see langword="true"/>.
    /// </summary>
    [TestMethod]
    public async Task RevokeAsync_ExistingKey_ReturnsTrue( ) {
        (ApiKey entity, string _) = await _service.CreateAsync(
            "To Revoke",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        bool result = await _service.RevokeAsync(
            entity.Id,
            TestContext.CancellationToken
        );

        Assert.IsTrue( result );

        ApiKey? revoked = await _dbContext.ApiKeys.FindAsync(
            [entity.Id],
            TestContext.CancellationToken
        );
        Assert.IsTrue( revoked?.IsRevoked );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.RevokeAsync"/> returns
    /// <see langword="false"/> when called with a <see cref="Guid"/> that
    /// does not correspond to any existing API key.
    /// </summary>
    [TestMethod]
    public async Task RevokeAsync_NonExistentKey_ReturnsFalse( ) {
        bool result = await _service.RevokeAsync(
            Guid.NewGuid( ),
            TestContext.CancellationToken
        );
        Assert.IsFalse( result );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.GetAllAsync"/> returns all
    /// API keys that have been created, confirming that the service
    /// correctly queries the full key set.
    /// </summary>
    [TestMethod]
    public async Task GetAllAsync_ReturnsCreatedKeys( ) {
        _ = await _service.CreateAsync(
            "Key1",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );
        _ = await _service.CreateAsync(
            "Key2",
            "Operator",
            "user-2",
            ct: TestContext.CancellationToken
        );

        IReadOnlyList<ApiKey> keys = await _service.GetAllAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, keys );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.GetByUserAsync"/> returns
    /// only the API keys created by the specified user ID, filtering out
    /// keys belonging to other users.
    /// </summary>
    [TestMethod]
    public async Task GetByUserAsync_FiltersCorrectly( ) {
        _ = await _service.CreateAsync(
            "User1 Key",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );
        _ = await _service.CreateAsync(
            "User2 Key",
            "Operator",
            "user-2",
            ct: TestContext.CancellationToken
        );

        IReadOnlyList<ApiKey> user1Keys = await _service.GetByUserAsync(
            "user-1",
            TestContext.CancellationToken
        );
        IReadOnlyList<ApiKey> user2Keys = await _service.GetByUserAsync(
            "user-2",
            TestContext.CancellationToken
        );

        Assert.HasCount( 1, user1Keys );
        Assert.AreEqual( "User1 Key", user1Keys[0].Name );
        Assert.HasCount( 1, user2Keys );
        Assert.AreEqual( "User2 Key", user2Keys[0].Name );
    }

    /// <summary>
    /// Verifies that two consecutive calls to
    /// <see cref="ApiKeyService.CreateAsync"/> produce distinct raw key
    /// strings, ensuring that each generated API key is
    /// cryptographically unique.
    /// </summary>
    [TestMethod]
    public async Task CreateAsync_GeneratesUniqueKeys( ) {
        (_, string rawKey1) = await _service.CreateAsync(
            "Key A",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );
        (_, string rawKey2) = await _service.CreateAsync(
            "Key B",
            "Admin",
            "user-1",
            ct: TestContext.CancellationToken
        );

        Assert.AreNotEqual( rawKey1, rawKey2 );
    }

    /// <summary>
    /// Verifies that <see cref="ApiKeyService.ValidateAsync"/> returns
    /// <see langword="null"/> when given a <see langword="null"/>, empty,
    /// or whitespace-only raw key string, handling edge cases gracefully.
    /// </summary>
    [TestMethod]
    public async Task ValidateAsync_NullOrEmpty_ReturnsNull( ) {
        Assert.IsNull(
            await _service.ValidateAsync(
                null!,
                TestContext.CancellationToken
            )
        );
        Assert.IsNull(
            await _service.ValidateAsync(
                "",
                TestContext.CancellationToken
            )
        );
        Assert.IsNull(
            await _service.ValidateAsync(
                "  ",
                TestContext.CancellationToken
            )
        );
    }
}
