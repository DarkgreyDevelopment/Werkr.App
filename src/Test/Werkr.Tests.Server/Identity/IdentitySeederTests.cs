using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Roles;
using Werkr.Server.Identity;
using Werkr.Server.Services;

namespace Werkr.Tests.Server.Identity;

[TestClass]
public class IdentitySeederTests {
    public TestContext TestContext { get; set; } = null!;
    private ServiceProvider _provider = null!;

    [TestInitialize]
    public async Task TestInit( ) {
        ServiceCollection services = new( );

        // Use a unique in-memory database per test to avoid cross-test contamination
        string dbName = $"IdentitySeederTests_{Guid.NewGuid( )}";
        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( b => b.AddProvider( NullLoggerProvider.Instance ) );

        _ = services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder( ).AddInMemoryCollection( ).Build( ) );

        // ServerConfigCache uses WerkrIdentityDbContext for config persistence
        _ = services.AddSingleton<ServerConfigCache>( );

        _provider = services.BuildServiceProvider( );

        // Initialize the config cache (creates the default config row)
        ServerConfigCache configCache = _provider.GetRequiredService<ServerConfigCache>( );
        await configCache.InitializeAsync( TestContext.CancellationToken );
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _provider.Dispose( );
    }

    [TestMethod]
    public async Task SeedAsync_CreatesDefaultRoles( ) {
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>( );

        foreach (string role in Enum.GetNames<DefaultRoles>( )) {
            Assert.IsTrue( await roleManager.RoleExistsAsync( role ),
                $"Role '{role}' should exist after seeding." );
        }
    }

    [TestMethod]
    public async Task SeedAsync_CreatesDefaultAdminUser( ) {
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope( );
        UserManager<WerkrUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<WerkrUser>>( );

        WerkrUser? admin = await userManager.FindByEmailAsync( "admin@werkr.local" );

        Assert.IsNotNull( admin, "Default admin user should be created." );
        Assert.AreEqual( "Default Admin", admin.Name );
        Assert.IsTrue( admin.Enabled, "Admin should be enabled." );
        Assert.IsTrue( admin.ChangePassword, "Admin should be flagged for password change." );
        Assert.IsTrue( admin.Requires2FA, "Admin should require MFA enrollment." );
        Assert.IsTrue( admin.EmailConfirmed, "Admin email should be confirmed." );
    }

    [TestMethod]
    public async Task SeedAsync_AdminHasAdminRole( ) {
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope( );
        UserManager<WerkrUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<WerkrUser>>( );

        WerkrUser? admin = await userManager.FindByEmailAsync( "admin@werkr.local" );
        Assert.IsNotNull( admin );

        IList<string> roles = await userManager.GetRolesAsync( admin );

        Assert.Contains( DefaultRoles.Admin.ToString( ), roles,
            "Admin user should be in Admin role." );
    }

    [TestMethod]
    public async Task SeedAsync_SecondCallDoesNotCreateDuplicateAdmin( ) {
        await IdentitySeeder.SeedAsync( _provider );
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope( );
        UserManager<WerkrUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<WerkrUser>>( );

        IList<WerkrUser> admins = await userManager.GetUsersInRoleAsync(
            DefaultRoles.Admin.ToString( ) );

        Assert.HasCount( 1, admins, "Should have exactly one admin after double-seed." );
    }

    [TestMethod]
    public async Task SeedAsync_SecondCallDoesNotCreateDuplicateRoles( ) {
        await IdentitySeeder.SeedAsync( _provider );
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>( );

        // Each role should exist exactly once
        foreach (string role in Enum.GetNames<DefaultRoles>( )) {
            Assert.IsTrue( await roleManager.RoleExistsAsync( role ),
                $"Role '{role}' should still exist after double-seed." );
        }
    }
}
