using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Common.Auth;
using Werkr.Data;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Services;
using Werkr.Server.Identity;
using Werkr.Server.Services;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for <see cref="PermissionService"/> and the seeded role-permission mappings.
/// </summary>
[TestClass]
public class PermissionServiceTests {
    public TestContext TestContext { get; set; } = null!;

    private ServiceProvider _provider = null!;

    [TestInitialize]
    public async Task TestInit( ) {
        ServiceCollection services = new( );
        string dbName = $"PermSvcTests_{Guid.NewGuid( )}";
        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        // ServerConfigCache requires WerkrDbContext
        _ = services.AddDbContext<WerkrDbContext>( options =>
            options.UseInMemoryDatabase( dbName + "_werkr" ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
                Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );
        _ = services.AddLogging( b => b.AddProvider( NullLoggerProvider.Instance ) );
        _ = services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder( ).AddInMemoryCollection( ).Build( ) );
        _ = services.AddSingleton<ServerConfigCache>( );
        _ = services.AddScoped<IPermissionService, PermissionService>( );
        _provider = services.BuildServiceProvider( );

        // Initialize the config cache (creates the default config row)
        ServerConfigCache configCache = _provider.GetRequiredService<ServerConfigCache>( );
        await configCache.InitializeAsync( TestContext.CancellationToken );
    }

    [TestCleanup]
    public void TestCleanup( ) => _provider.Dispose( );

    [TestMethod]
    public async Task Admin_HasAllPermissions( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        IReadOnlySet<Permission> perms = await svc.GetPermissionsAsync(
            ["Admin"], TestContext.CancellationToken );

        foreach (Permission p in Enum.GetValues<Permission>( )) {
            Assert.Contains( p, perms, $"Admin should have permission {p}." );
        }
    }

    [TestMethod]
    public async Task Operator_HasReadAndExecute( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsTrue( await svc.HasPermissionAsync( ["Operator"], Permission.Read, TestContext.CancellationToken ) );
        Assert.IsTrue( await svc.HasPermissionAsync( ["Operator"], Permission.Execute, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Operator"], Permission.Admin, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Operator"], Permission.Delete, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task Viewer_HasOnlyRead( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsTrue( await svc.HasPermissionAsync( ["Viewer"], Permission.Read, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Viewer"], Permission.Create, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Viewer"], Permission.Execute, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task UnknownRole_HasNoPermissions( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        IReadOnlySet<Permission> perms = await svc.GetPermissionsAsync(
            ["NonExistentRole"], TestContext.CancellationToken );
        Assert.IsEmpty( perms );
    }

    [TestMethod]
    public async Task EmptyRoles_ReturnsFalse( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsFalse( await svc.HasPermissionAsync(
            [], Permission.Read, TestContext.CancellationToken ) );
    }

    [TestMethod]
    public async Task MultipleRoles_UnionPermissions( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        // Viewer + Operator => Read + Execute
        IReadOnlySet<Permission> perms = await svc.GetPermissionsAsync(
            ["Viewer", "Operator"], TestContext.CancellationToken );
        Assert.Contains( Permission.Read, perms );
        Assert.Contains( Permission.Execute, perms );
    }
}
