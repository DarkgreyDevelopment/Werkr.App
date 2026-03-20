using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Services;

namespace Werkr.Tests.Data.Unit.Identity;

/// <summary>
/// Unit tests for <see cref="PasswordHistoryService"/>, validating that password history
/// recording and trimming correctly enforce the configured <see cref="PasswordHistoryOptions.HistoryCount"/> limit.
/// </summary>
[TestClass]
public class PasswordHistoryServiceTests {
    private SqliteConnection _connection = null!;
    private SqliteWerkrIdentityDbContext _db = null!;

    /// <summary>Gets or sets the MSTest test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Initializes an in-memory SQLite identity database.</summary>
    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        DbContextOptions<SqliteWerkrIdentityDbContext> options =
            new DbContextOptionsBuilder<SqliteWerkrIdentityDbContext>( )
                .UseSqlite( _connection )
                .Options;

        _db = new SqliteWerkrIdentityDbContext( options );
        _ = _db.Database.EnsureCreated( );
    }

    /// <summary>Disposes the database context and SQLite connection.</summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _db?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Seeds exactly <paramref name="limit"/> password history rows, calls <see cref="PasswordHistoryService.RecordAsync"/>
    /// one more time, and asserts the total count is still exactly <paramref name="limit"/>.
    /// </summary>
    [TestMethod]
    [DataRow( 3 )]
    [DataRow( 5 )]
    public async Task RecordAsync_AtLimit_TrimsToExactlyLimit( int limit ) {
        CancellationToken ct = TestContext.CancellationToken;
        string userId = SeedUser( );

        IOptions<PasswordHistoryOptions> opts = Options.Create(
            new PasswordHistoryOptions { HistoryCount = limit } );

        PasswordHistoryService service = new( _db, opts );

        // Seed exactly `limit` rows
        for (int i = 0; i < limit; i++) {
            await service.RecordAsync( userId, $"hash_{i}", ct );
        }

        int countBefore = await _db.PasswordHistory.CountAsync( h => h.UserId == userId, ct );
        Assert.AreEqual( limit, countBefore, "Seeding should produce exactly 'limit' rows." );

        // Record one more — should still be exactly `limit`
        await service.RecordAsync( userId, "hash_new", ct );

        int countAfter = await _db.PasswordHistory.CountAsync( h => h.UserId == userId, ct );
        Assert.AreEqual( limit, countAfter, "After recording beyond limit, count should still equal limit." );
    }

    /// <summary>
    /// Verifies that the most recent hash is retained after trimming (LIFO ordering).
    /// </summary>
    [TestMethod]
    public async Task RecordAsync_RetainsMostRecentHash( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string userId = SeedUser( );
        int limit = 3;

        IOptions<PasswordHistoryOptions> opts = Options.Create(
            new PasswordHistoryOptions { HistoryCount = limit } );

        PasswordHistoryService service = new( _db, opts );

        for (int i = 0; i < limit; i++) {
            await service.RecordAsync( userId, $"hash_{i}", ct );
        }

        await service.RecordAsync( userId, "hash_latest", ct );

        PasswordHistory? latest = await _db.PasswordHistory
            .Where( h => h.UserId == userId )
            .OrderByDescending( h => h.CreatedUtc )
            .FirstOrDefaultAsync( ct );

        Assert.IsNotNull( latest );
        Assert.AreEqual( "hash_latest", latest.PasswordHash );
    }

    /// <summary>Seeds a minimal user row and returns the user ID.</summary>
    private string SeedUser( ) {
        string userId = Guid.NewGuid( ).ToString( );
        _ = _db.Users.Add( new WerkrUser {
            Id = userId,
            UserName = $"test_{userId[..8]}",
            NormalizedUserName = $"TEST_{userId[..8]}",
            Email = $"{userId[..8]}@test.local",
            NormalizedEmail = $"{userId[..8]}@TEST.LOCAL",
        } );
        _ = _db.SaveChanges( );
        return userId;
    }
}
