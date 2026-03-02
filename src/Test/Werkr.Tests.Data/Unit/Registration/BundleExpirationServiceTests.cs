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

[TestClass]
public class BundleExpirationServiceTests {
    private SqliteConnection _connection = null!;
    private ServiceProvider _serviceProvider = null!;

    public TestContext TestContext { get; set; } = null!;

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

    [TestCleanup]
    public void TestCleanup( ) {
        _serviceProvider?.Dispose( );
        _connection?.Dispose( );
    }

    [TestMethod]
    public async Task ExecuteAsync_ExpiredPendingBundles_TransitionsToExpired( ) {
        // Seed an expired pending bundle
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add( new RegistrationBundle {
                ConnectionName = "Stale",
                BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                Status = RegistrationStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours( -1 ),
                KeySize = 4096,
            } );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        // Run the background service and poll until the bundle transitions
        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 ) );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );

        // Poll until expired or timeout (max 5 seconds)
        RegistrationStatus status = RegistrationStatus.Pending;
        DateTime deadline = DateTime.UtcNow.AddSeconds( 5 );
        while (status == RegistrationStatus.Pending && DateTime.UtcNow < deadline) {
            await Task.Delay( 100, TestContext.CancellationToken );
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
        Assert.AreEqual( RegistrationStatus.Expired, status );
    }

    [TestMethod]
    public async Task ExecuteAsync_NonPendingBundles_NotModified( ) {
        // Seed a completed bundle (expired in the past but already completed)
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add( new RegistrationBundle {
                ConnectionName = "AlreadyDone",
                BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                Status = RegistrationStatus.Completed,
                ExpiresAt = DateTime.UtcNow.AddHours( -1 ),
                KeySize = 4096,
            } );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 ) );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );
        await Task.Delay( 300, TestContext.CancellationToken );
        cts.Cancel( );
        await service.StopAsync( TestContext.CancellationToken );

        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            RegistrationBundle bundle = await db.RegistrationBundles
                .SingleAsync( TestContext.CancellationToken );
            Assert.AreEqual( RegistrationStatus.Completed, bundle.Status );
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_UnexpiredBundles_NotModified( ) {
        // Seed a pending bundle that has not yet expired
        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            _ = db.RegistrationBundles.Add( new RegistrationBundle {
                ConnectionName = "Fresh",
                BundleId = EncryptionProvider.GenerateRandomBytes( 16 ),
                Status = RegistrationStatus.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours( 24 ),
                KeySize = 4096,
            } );
            _ = await db.SaveChangesAsync( TestContext.CancellationToken );
        }

        IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>( );
        BundleExpirationService service = new(
            scopeFactory,
            NullLogger<BundleExpirationService>.Instance,
            interval: TimeSpan.FromMilliseconds( 50 ) );

        using CancellationTokenSource cts = new( );
        await service.StartAsync( cts.Token );
        await Task.Delay( 300, TestContext.CancellationToken );
        cts.Cancel( );
        await service.StopAsync( TestContext.CancellationToken );

        using (IServiceScope scope = _serviceProvider.CreateScope( )) {
            WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
            RegistrationBundle bundle = await db.RegistrationBundles
                .SingleAsync( TestContext.CancellationToken );
            Assert.AreEqual( RegistrationStatus.Pending, bundle.Status );
        }
    }
}
