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
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> used for cancellation token access and test run metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The fully configured <see cref="ServiceProvider"/> containing Identity, EF Core, <see
    /// cref="PermissionService"/>, and <see cref="ServerConfigCache"/> services used by all test methods.
    /// </summary>
    private ServiceProvider _provider = null!;

    /// <summary>
    /// Initializes a fresh in-memory database for both <see cref="WerkrIdentityDbContext"/> and <see
    /// cref="WerkrDbContext"/>, registers Identity services, <see cref="PermissionService"/>, logging, configuration,
    /// and <see cref="ServerConfigCache"/>, then eagerly initializes the config cache.
    /// </summary>
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
                Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions
        )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );
        _ = services.AddLogging( b => b.AddProvider( NullLoggerProvider.Instance ) );
        _ = services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder( ).AddInMemoryCollection( ).Build( )
        );
        _ = services.AddSingleton<ServerConfigCache>( );
        _ = services.AddScoped<IPermissionService, PermissionService>( );
        _provider = services.BuildServiceProvider( );

        // Initialize the config cache (creates the default config row)
        ServerConfigCache configCache = _provider.GetRequiredService<ServerConfigCache>( );
        await configCache.InitializeAsync( TestContext.CancellationToken );
    }

    /// <summary>
    /// Disposes the <see cref="ServiceProvider"/> and all scoped services after each test.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) => _provider.Dispose( );

    /// <summary>
    /// Verifies that the "Admin" role possesses every <see cref="Permission"/> value defined in the <see
    /// cref="Permission"/> enum, confirming full administrative access.
    /// </summary>
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

    /// <summary>
    /// Verifies that the "Operator" role has <see cref="Permission.Read"/> and <see cref="Permission.Execute"/> but
    /// does not have <see cref="Permission.Admin"/> or <see cref="Permission.Delete"/>, confirming the operator role's
    /// limited scope.
    /// </summary>
    [TestMethod]
    public async Task Operator_HasReadAndExecute( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsTrue( await svc.HasPermissionAsync(
            ["Operator"],
            Permission.Read,
            TestContext.CancellationToken
        ) );
        Assert.IsTrue( await svc.HasPermissionAsync(
            ["Operator"],
            Permission.Execute,
            TestContext.CancellationToken
        ) );
        Assert.IsFalse( await svc.HasPermissionAsync(
            ["Operator"],
            Permission.Admin,
            TestContext.CancellationToken
        ) );
        Assert.IsFalse( await svc.HasPermissionAsync(
            ["Operator"],
            Permission.Delete,
            TestContext.CancellationToken
        ) );
    }

    /// <summary>
    /// Verifies that the "Viewer" role has only <see cref="Permission.Read"/> and lacks <see
    /// cref="Permission.Create"/> and <see cref="Permission.Execute"/>, confirming read-only access.
    /// </summary>
    [TestMethod]
    public async Task Viewer_HasOnlyRead( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsTrue( await svc.HasPermissionAsync( ["Viewer"], Permission.Read, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Viewer"], Permission.Create, TestContext.CancellationToken ) );
        Assert.IsFalse( await svc.HasPermissionAsync( ["Viewer"], Permission.Execute, TestContext.CancellationToken ) );
    }

    /// <summary>
    /// Verifies that a role name that does not exist in the seeded role data returns an empty permission set, ensuring
    /// no permissions are granted by default for unknown roles.
    /// </summary>
    [TestMethod]
    public async Task UnknownRole_HasNoPermissions( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        IReadOnlySet<Permission> perms = await svc.GetPermissionsAsync(
            ["NonExistentRole"], TestContext.CancellationToken );
        Assert.IsEmpty( perms );
    }

    /// <summary>
    /// Verifies that <see cref="HasPermissionAsync"/> returns <see langword="false"/> when called with an empty role
    /// list, confirming that unauthenticated or roleless users are denied access.
    /// </summary>
    [TestMethod]
    public async Task EmptyRoles_ReturnsFalse( ) {
        await IdentitySeeder.SeedAsync( _provider );
        using IServiceScope scope = _provider.CreateScope( );
        IPermissionService svc = scope.ServiceProvider.GetRequiredService<IPermissionService>( );

        Assert.IsFalse( await svc.HasPermissionAsync(
            [], Permission.Read, TestContext.CancellationToken ) );
    }

    /// <summary>
    /// Verifies that when a user belongs to multiple roles (e.g., "Viewer" and "Operator"), the resulting permission
    /// set is the union of all individual role permissions, granting both <see cref="Permission.Read"/> and <see
    /// cref="Permission.Execute"/>.
    /// </summary>
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
