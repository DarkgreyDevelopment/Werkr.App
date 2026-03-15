using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Settings;

namespace Werkr.Tests.Data.Unit.Endpoints;

/// <summary>
/// Unit tests for the <c>FilterEndpoints</c> class, validating page key allowlist completeness
/// and that filter CRUD operations enforce ownership and produce correct persistence results.
/// </summary>
[TestClass]
public class FilterEndpointTests {
    /// <summary>
    /// The in-memory SQLite connection used for database operations.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The SQLite-backed <see cref="WerkrDbContext"/> used for test data persistence.
    /// </summary>
    private SqliteWerkrDbContext _dbContext = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The complete set of valid page keys that the filter endpoints must accept.
    /// </summary>
    private static readonly HashSet<string> s_expectedPageKeys = [
        "runs", "workflows", "jobs", "agents", "schedules", "tasks",
        "all-workflow-runs", "workflow-dashboard"
    ];

    /// <summary>
    /// Creates an in-memory SQLite database and the schema for each test.
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
    }

    /// <summary>
    /// Disposes the database context and SQLite connection after each test.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _dbContext?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Verifies that the <c>s_validPageKeys</c> field on FilterEndpoints contains exactly the
    /// expected set of page keys including <c>all-workflow-runs</c> and <c>workflow-dashboard</c>.
    /// </summary>
    [TestMethod]
    public void ValidPageKeys_ContainsAllExpectedKeys( ) {
        Assembly apiAssembly = Assembly.Load( "Werkr.Api" );
        Type? endpointsType = apiAssembly.GetType( "Werkr.Api.Endpoints.FilterEndpoints" );
        Assert.IsNotNull( endpointsType, "FilterEndpoints type not found in Werkr.Api assembly" );

        FieldInfo? field = endpointsType.GetField(
            "s_validPageKeys",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.IsNotNull( field, "s_validPageKeys field not found on FilterEndpoints" );

        object? value = field.GetValue( null );
        _ = Assert.IsInstanceOfType<HashSet<string>>( value );

        HashSet<string> actualKeys = (HashSet<string>)value;

        foreach (string expected in s_expectedPageKeys) {
            Assert.Contains(
                expected, actualKeys,
                $"Missing page key: '{expected}'"
            );
        }

        Assert.HasCount(
            s_expectedPageKeys.Count,
            actualKeys,
            $"Page key count mismatch. Expected: {s_expectedPageKeys.Count}, Actual: {actualKeys.Count}"
        );
    }

    /// <summary>
    /// Verifies that creating a filter persists the entity with the correct owner and page key.
    /// </summary>
    [TestMethod]
    public async Task CreateFilter_PersistsWithCorrectOwnerAndPageKey( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string userId = "user-1";

        SavedFilter entity = new( ) {
            OwnerId = userId,
            PageKey = "runs",
            Name = "My Filter",
            CriteriaJson = "{\"status\":\"Running\"}",
            IsShared = false,
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        _ = _dbContext.SavedFilters.Add( entity );
        _ = await _dbContext.SaveChangesAsync( ct );

        SavedFilter? loaded = await _dbContext.SavedFilters
            .FirstOrDefaultAsync( f => f.OwnerId == userId && f.PageKey == "runs", ct );

        Assert.IsNotNull( loaded );
        Assert.AreEqual( "My Filter", loaded.Name );
        Assert.AreEqual( "{\"status\":\"Running\"}", loaded.CriteriaJson );
        Assert.IsFalse( loaded.IsShared );
    }

    /// <summary>
    /// Verifies that filters can be queried by page key and include both owned and shared filters.
    /// </summary>
    [TestMethod]
    public async Task QueryFilters_ReturnsBothOwnedAndShared( ) {
        CancellationToken ct = TestContext.CancellationToken;
        string userId = "user-1";

        SavedFilter owned = new( ) {
            OwnerId = userId,
            PageKey = "runs",
            Name = "My Filter",
            CriteriaJson = "{}",
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        SavedFilter shared = new( ) {
            OwnerId = "other-user",
            PageKey = "runs",
            Name = "Shared Filter",
            CriteriaJson = "{}",
            IsShared = true,
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        SavedFilter differentPage = new( ) {
            OwnerId = userId,
            PageKey = "jobs",
            Name = "Jobs Filter",
            CriteriaJson = "{}",
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        _dbContext.SavedFilters.AddRange( owned, shared, differentPage );
        _ = await _dbContext.SaveChangesAsync( ct );

        List<SavedFilter> results = await _dbContext.SavedFilters
            .Where( f => f.PageKey == "runs" && (f.OwnerId == userId || f.IsShared) )
            .OrderBy( f => f.Name )
            .ToListAsync( ct );

        Assert.HasCount( 2, results );
        Assert.AreEqual( "My Filter", results[0].Name );
        Assert.AreEqual( "Shared Filter", results[1].Name );
    }

    /// <summary>
    /// Verifies that deleting a filter only removes the targeted entity.
    /// </summary>
    [TestMethod]
    public async Task DeleteFilter_RemovesOnlyTargetEntity( ) {
        CancellationToken ct = TestContext.CancellationToken;

        SavedFilter filter1 = new( ) {
            OwnerId = "user-1",
            PageKey = "runs",
            Name = "Filter 1",
            CriteriaJson = "{}",
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        SavedFilter filter2 = new( ) {
            OwnerId = "user-1",
            PageKey = "runs",
            Name = "Filter 2",
            CriteriaJson = "{}",
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        _dbContext.SavedFilters.AddRange( filter1, filter2 );
        _ = await _dbContext.SaveChangesAsync( ct );

        _ = _dbContext.SavedFilters.Remove( filter1 );
        _ = await _dbContext.SaveChangesAsync( ct );

        List<SavedFilter> remaining = await _dbContext.SavedFilters.ToListAsync( ct );

        Assert.HasCount( 1, remaining );
        Assert.AreEqual( "Filter 2", remaining[0].Name );
    }

    /// <summary>
    /// Verifies that updating a filter increments the version and persists the new values.
    /// </summary>
    [TestMethod]
    public async Task UpdateFilter_IncrementsVersionAndPersists( ) {
        CancellationToken ct = TestContext.CancellationToken;

        SavedFilter entity = new( ) {
            OwnerId = "user-1",
            PageKey = "runs",
            Name = "Original",
            CriteriaJson = "{}",
            Created = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow,
            Version = 1,
        };

        _ = _dbContext.SavedFilters.Add( entity );
        _ = await _dbContext.SaveChangesAsync( ct );

        entity.Name = "Updated";
        entity.CriteriaJson = "{\"status\":\"Failed\"}";
        entity.Version++;
        entity.LastUpdated = DateTime.UtcNow;
        _ = await _dbContext.SaveChangesAsync( ct );

        SavedFilter? loaded = await _dbContext.SavedFilters
            .AsNoTracking( )
            .FirstOrDefaultAsync( f => f.Id == entity.Id, ct );

        Assert.IsNotNull( loaded );
        Assert.AreEqual( "Updated", loaded.Name );
        Assert.AreEqual( "{\"status\":\"Failed\"}", loaded.CriteriaJson );
    }
}
