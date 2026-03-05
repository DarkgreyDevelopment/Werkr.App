using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Models;
using Werkr.Core.Cryptography;
using Werkr.Core.Registration;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Tests.Data.Unit.Registration;

/// <summary>
/// Contains unit tests for the <see cref="BundleExpirationService"/> background service defined in Werkr.Core.
/// Validates that expired pending bundles are transitioned, non-pending bundles are left alone, and unexpired bundles
/// remain pending.
/// </summary>
[TestClass]
public class BundleExpirationServiceTests {
    /// <summary>
    /// The in-memory SQLite connection kept open for the duration of each test.
    /// </summary>
    private SqliteConnection _connection = null!;
    /// <summary>
    /// The service provider supplying scoped <see cref="WerkrDbContext"/> instances.
    /// </summary>
    private ServiceProvider _serviceProvider = null!;

    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> providing per-test cancellation tokens and metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates an in-memory SQLite database and registers <see cref="WerkrDbContext"/> services.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        _connection = new SqliteConnection( "DataSource=:memory:" );
        _connection.Open( );

        ServiceCollection services = new( );
        _ = services.AddDbContext<SqliteWerkrDbContext>( opt => opt.UseSqlite( _connection ) );
        _ = services.AddScoped<WerkrDbContext>( sp => sp.GetRequiredService<SqliteWerkrDbContext>( ) );
        _serviceProvider = services.BuildServiceProvider( );

        // Create schema
        using IServiceScope scope = _serviceProvider.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        _ = db.Database.EnsureCreated( );
    }

    /// <summary>
    /// Disposes the service provider and SQLite connection.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider?.Dispose( );
        _connection?.Dispose( );
    }

    /// <summary>
    /// Verifies that a pending bundle past its expiration is transitioned to <see cref="RegistrationStatus.Expired"/>.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_ExpiredPendingBundles_TransitionsToExpired( ) {
        // Seed an expired pending bundle
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add(
                new RegistrationBundle {
                    ConnectionName = "Stale",
                    BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                    Status = RegistrationStatus.Pending,
                    ExpiresAt = DateTime.UtcNow.AddHours( -1 ),
                    KeySize = 4096,
                }
            );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        // Run the background service and poll until the bundle transitions
        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 )
        );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );

        // Poll until expired or timeout (max 5 seconds)
        RegistrationStatus status = RegistrationStatus.Pending;
        DateTime deadline = DateTime.UtcNow.AddSeconds( 5 );
        while (status == RegistrationStatus.Pending && DateTime.UtcNow < deadline) {
            await Task.Delay(
                100,
                TestContext.CancellationToken
            );
            using IServiceScope pollScope = _serviceProvider.CreateScope( );
            WerkrDbContext pollDb = pollScope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            RegistrationBundle polled = await pollDb.RegistrationBundles
                .AsNoTracking( )
                .SingleAsync( TestContext.CancellationToken );
            status = polled.Status;
        }

        cts.Cancel( );
        await service.StopAsync( TestContext.CancellationToken );

        // Verify
        Assert.AreEqual(
            RegistrationStatus.Expired,
            status
        );
    }

    /// <summary>
    /// Verifies that a non-pending bundle (e.g., <see cref="Completed"/>) is not modified by the expiration service.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_NonPendingBundles_NotModified( ) {
        // Seed a completed bundle (expired in the past but already completed)
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add(
                new RegistrationBundle {
                    ConnectionName = "AlreadyDone",
                    BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                    Status = RegistrationStatus.Completed,
                    ExpiresAt = DateTime.UtcNow.AddHours( -1 ),
                    KeySize = 4096,
                }
            );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 )
        );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );
        await Task.Delay(
            300,
            TestContext.CancellationToken
        );
        cts.Cancel( );
        await service.StopAsync( TestContext.CancellationToken );

        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            RegistrationBundle bundle = await db.RegistrationBundles
                .SingleAsync( TestContext.CancellationToken );
            Assert.AreEqual(
                RegistrationStatus.Completed,
                bundle.Status
            );
        }
    }

    /// <summary>
    /// Verifies that a pending bundle that has not yet expired is not modified by the expiration service.
    /// </summary>
    [TestMethod]
    public async Task ExecuteAsync_UnexpiredBundles_NotModified( ) {
        // Seed a pending bundle that has not yet expired
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add(
                new RegistrationBundle {
                    ConnectionName = "Fresh",
                    BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                    Status = RegistrationStatus.Pending,
                    ExpiresAt = DateTime.UtcNow.AddHours( 24 ),
                    KeySize = 4096,
                }
            );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 )
        );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );
        await Task.Delay(
            300,
            TestContext.CancellationToken
        );
        cts.Cancel( );
        await service.StopAsync( TestContext.CancellationToken );

        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            RegistrationBundle bundle = await db.RegistrationBundles
                .SingleAsync( TestContext.CancellationToken );
            Assert.AreEqual(
                RegistrationStatus.Pending,
                bundle.Status
            );
        }
    }
}
