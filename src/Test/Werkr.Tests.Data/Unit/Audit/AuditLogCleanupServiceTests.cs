using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Werkr.Api.Services;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Audit;

namespace Werkr.Tests.Data.Unit.Audit;

[TestClass]
public class AuditLogCleanupServiceTests {

    private SqliteConnection _connection = null!;
    private DbContextOptions<SqliteWerkrDbContext> _contextOptions = null!;
    private AuditEventTypeRegistry _registry = null!;
    private IServiceScopeFactory _scopeFactory = null!;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        _contextOptions = new DbContextOptionsBuilder<SqliteWerkrDbContext>( )
            .UseSqlite( _connection )
            .Options;

        // Create schema once
        using (SqliteWerkrDbContext initCtx = new( _contextOptions )) {
            _ = initCtx.Database.EnsureCreated( );
        }

        _registry = new AuditEventTypeRegistry( );
        _ = _registry.RegisterCoreAuditEvents( );

        ServiceCollection services = new( );
        _ = services.AddSingleton<IAuditEventTypeRegistry>( _registry );
        _ = services.AddScoped<WerkrDbContext>( _ => new SqliteWerkrDbContext( _contextOptions ) );
        _ = services.AddScoped<IAuditService>( sp => new AuditService(
            sp.GetRequiredService<WerkrDbContext>( ),
            sp.GetRequiredService<IAuditEventTypeRegistry>( ),
            NullLogger<AuditService>.Instance
        ) );

        ServiceProvider provider = services.BuildServiceProvider( );
        _scopeFactory = provider.GetRequiredService<IServiceScopeFactory>( );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _connection?.Dispose( );
    }

    private SqliteWerkrDbContext CreateContext( ) => new( _contextOptions );

    [TestMethod]
    public async Task Cleanup_DeletesRecordsOlderThanRetention( ) {
        CancellationToken ct = TestContext.CancellationToken;

        // Seed with a separate context
        using (SqliteWerkrDbContext seedCtx = CreateContext( )) {
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow.AddDays( -400 ) ) );
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow.AddDays( -500 ) ) );
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow ) );
            _ = await seedCtx.SaveChangesAsync( ct );
        }

        AuditLogCleanupService service = CreateService( retentionDays: 365 );
        await InvokeCleanupAsync( service, ct );

        // Assert with a fresh context
        using SqliteWerkrDbContext assertCtx = CreateContext( );
        int remaining = await assertCtx.AuditEvents.CountAsync( ct );
        Assert.AreEqual( 2, remaining, "Expected 1 recent event + 1 summary event after cleanup." );
    }

    [TestMethod]
    public async Task Cleanup_CreatesSummaryEvent( ) {
        CancellationToken ct = TestContext.CancellationToken;

        using (SqliteWerkrDbContext seedCtx = CreateContext( )) {
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow.AddDays( -400 ), category: "Security" ) );
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow.AddDays( -500 ), category: "Agent" ) );
            _ = await seedCtx.SaveChangesAsync( ct );
        }

        AuditLogCleanupService service = CreateService( retentionDays: 365 );
        await InvokeCleanupAsync( service, ct );

        using SqliteWerkrDbContext assertCtx = CreateContext( );
        AuditEvent? summary = await assertCtx.AuditEvents
            .FirstOrDefaultAsync( e => e.EventTypeId == "audit.retention.cleanup", ct );

        Assert.IsNotNull( summary, "Expected a summary audit event after cleanup." );
        Assert.AreEqual( "Cleanup", summary.ActionPerformed );
        Assert.Contains( "deletedCount", summary.Details );
        Assert.Contains( "earliestTimestamp", summary.Details );
        Assert.Contains( "latestTimestamp", summary.Details );
        Assert.Contains( "categoryCounts", summary.Details );
    }

    [TestMethod]
    public async Task Cleanup_NoExpiredRecords_SkipsDeletion( ) {
        CancellationToken ct = TestContext.CancellationToken;

        using (SqliteWerkrDbContext seedCtx = CreateContext( )) {
            _ = seedCtx.AuditEvents.Add( MakeEvent( DateTime.UtcNow ) );
            _ = await seedCtx.SaveChangesAsync( ct );
        }

        AuditLogCleanupService service = CreateService( retentionDays: 365 );
        await InvokeCleanupAsync( service, ct );

        using SqliteWerkrDbContext assertCtx = CreateContext( );
        bool hasSummary = await assertCtx.AuditEvents
            .AnyAsync( e => e.EventTypeId == "audit.retention.cleanup", ct );

        Assert.IsFalse( hasSummary, "Expected no summary event when nothing was deleted." );
        Assert.AreEqual( 1, await assertCtx.AuditEvents.CountAsync( ct ) );
    }

    private static AuditEvent MakeEvent( DateTime timestampUtc, string category = "Security" ) => new( ) {
        EventTypeId = "auth.login.success",
        EventCategory = category,
        SourceModule = "identity",
        ActorId = "test-user",
        ActorType = ActorType.User,
        EntityType = "User",
        EntityId = "test-user",
        ActionPerformed = "Login",
        Details = "{}",
        TimestampUtc = timestampUtc
    };

    private AuditLogCleanupService CreateService( int retentionDays ) {
        AuditLogOptions opts = new( ) { RetentionDays = retentionDays, SweepIntervalMinutes = 1440 };
        return new AuditLogCleanupService(
            _scopeFactory,
            Options.Create( opts ),
            NullLogger<AuditLogCleanupService>.Instance
        );
    }

    /// <summary>
    /// Invokes the private CleanupAsync method via reflection since ExecuteAsync
    /// is designed for long-running background execution.
    /// </summary>
    private static async Task InvokeCleanupAsync( AuditLogCleanupService service, CancellationToken ct ) {
        System.Reflection.MethodInfo? method = typeof( AuditLogCleanupService )
            .GetMethod( "CleanupAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
        Assert.IsNotNull( method, "CleanupAsync method not found." );
        await (Task)method.Invoke( service, [ct] )!;
    }
}
