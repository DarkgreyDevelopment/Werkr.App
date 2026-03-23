using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Werkr.Common.Configuration;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Extensions;
using Werkr.Data.Identity.Roles;
using Werkr.Data.Identity.Services;
using Werkr.Server.Identity;
using Werkr.Server.Services;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for the <see cref="IdentitySeeder"/> class defined in the <c>Werkr.Server</c> project. Validates that
/// the seeding process correctly creates the default roles defined by <see cref="DefaultRoles"/>, creates the default
/// admin user with expected properties, assigns the admin role, and that repeated seed invocations are idempotent (no
/// duplicates created). Uses an in-memory <see cref="WerkrIdentityDbContext"/> with a fully configured identity
/// service provider.
/// </summary>
[TestClass]
public class IdentitySeederTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> used for cancellation token access and test run metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;
    /// <summary>
    /// The fully configured <see cref="ServiceProvider"/> containing Identity, EF Core, logging, configuration, and
    /// <see cref="ServerConfigCache"/> services used by all test methods.
    /// </summary>
    private ServiceProvider _provider = null!;

    /// <summary>
    /// Initializes a fresh in-memory database, registers all required services (Identity, <see
    /// cref="WerkrIdentityDbContext"/>, logging, configuration, and <see cref="ServerConfigCache"/>), and eagerly
    /// initializes the <see cref="ServerConfigCache"/> before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInit( ) {
        ServiceCollection services = new( );

        // Use a unique in-memory database per test to avoid cross-test contamination
        string dbName = $"IdentitySeederTests_{Guid.NewGuid( )}";
        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            IdentityExtensions.ConfigureIdentityOptions
        )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( b => b.AddProvider( NullLoggerProvider.Instance ) );

        _ = services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder( ).AddInMemoryCollection( ).Build( )
        );

        // Password history service (used by IdentitySeeder to record initial passwords)
        _ = services.Configure<PasswordHistoryOptions>( _ => { } );
        _ = services.AddScoped<PasswordHistoryService>( );

        // ServerConfigCache uses WerkrIdentityDbContext for config persistence
        _ = services.AddSingleton<ServerConfigCache>( );

        _provider = services.BuildServiceProvider( );

        // Initialize the config cache (creates the default config row)
        ServerConfigCache configCache = _provider.GetRequiredService<ServerConfigCache>( );
        await configCache.InitializeAsync( TestContext.CancellationToken );
    }

    /// <summary>
    /// Disposes the <see cref="ServiceProvider"/> and all scoped services after each test.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _provider.Dispose( );
    }

    /// <summary>
    /// Verifies that <see cref="IdentitySeeder.SeedAsync"/> creates all roles defined in the <see
    /// cref="DefaultRoles"/> enum from the <c>Werkr.Data.Identity</c> project, confirming each role exists in the <see
    /// cref="RoleManager"/> after seeding.
    /// </summary>
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

    /// <summary>
    /// Verifies that <see cref="IdentitySeeder.SeedAsync"/> creates a default admin user with the email
    /// "admin@werkr.local", display name "Default Admin", and the appropriate flags set: <see cref="WerkrUser.Enabled"/> = <see
    /// langword="true"/>, <see cref="WerkrUser.ChangePassword"/> = <see langword="true"/>, <see cref="WerkrUser.Requires2FA"/> = <see
    /// langword="true"/>, and <see cref="WerkrUser.EmailConfirmed"/> = <see langword="true"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that the default admin user created by <see cref="IdentitySeeder.SeedAsync"/> is assigned to the
    /// "Admin" role as defined in <see cref="DefaultRoles"/>.
    /// </summary>
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

    /// <summary>
    /// Verifies that calling <see cref="IdentitySeeder.SeedAsync"/> twice does not create a duplicate admin user,
    /// confirming the seeder's idempotency for user creation.
    /// </summary>
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

    /// <summary>
    /// Verifies that calling <see cref="IdentitySeeder.SeedAsync"/> twice does not create duplicate roles, confirming
    /// the seeder's idempotency for role creation.
    /// </summary>
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

    /// <summary>
    /// Verifies that when <c>Werkr:SeedTestOperator</c> is not configured (the default), no test operator account is
    /// created by <see cref="IdentitySeeder.SeedAsync"/>. This ensures production deployments are unaffected.
    /// </summary>
    [TestMethod]
    public async Task SeedAsync_TestOperator_NotCreatedByDefault( ) {
        await IdentitySeeder.SeedAsync( _provider );

        using IServiceScope scope = _provider.CreateScope();
        UserManager<WerkrUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<WerkrUser>>();

        WerkrUser? operatorUser = await userManager.FindByEmailAsync("operator@werkr.local");

        Assert.IsNull( operatorUser,
            "Test operator should not be created when Werkr:SeedTestOperator is not configured." );
    }

    /// <summary>
    /// Verifies that when <c>Werkr:SeedTestOperator</c> is <c>true</c> and a valid password is provided via
    /// <c>Werkr:TestOperatorPassword</c>, <see cref="IdentitySeeder.SeedAsync"/> creates a test operator account with
    /// the expected properties: display name "Test Operator", <see cref="WerkrUser.Enabled"/> = <see
    /// langword="true"/>, <see cref="WerkrUser.ChangePassword"/> = <see langword="false"/>, <see
    /// cref="WerkrUser.Requires2FA"/> = <see langword="false"/>, and <see cref="WerkrUser.EmailConfirmed"/> = <see
    /// langword="true"/>.
    /// </summary>
    [TestMethod]
    public async Task SeedAsync_CreatesTestOperator_WhenConfigured( ) {
        ServiceProvider provider = BuildProviderWithTestOperatorConfig();
        try {
            await IdentitySeeder.SeedAsync( provider );

            using IServiceScope scope = provider.CreateScope();
            UserManager<WerkrUser> userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<WerkrUser>>();

            WerkrUser? operatorUser = await userManager.FindByEmailAsync("operator@werkr.local");

            Assert.IsNotNull( operatorUser, "Test operator should be created when configured." );
            Assert.AreEqual( "Test Operator", operatorUser.Name );
            Assert.IsTrue( operatorUser.Enabled, "Operator should be enabled." );
            Assert.IsFalse( operatorUser.ChangePassword, "Operator should not require password change." );
            Assert.IsFalse( operatorUser.Requires2FA, "Operator should not require 2FA." );
            Assert.IsTrue( operatorUser.EmailConfirmed, "Operator email should be confirmed." );
        } finally {
            provider.Dispose( );
        }
    }

    /// <summary>
    /// Verifies that the test operator account created by <see cref="IdentitySeeder.SeedAsync"/> is assigned to the
    /// "Operator" role as defined in <see cref="DefaultRoles"/>.
    /// </summary>
    [TestMethod]
    public async Task SeedAsync_TestOperator_HasOperatorRole( ) {
        ServiceProvider provider = BuildProviderWithTestOperatorConfig();
        try {
            await IdentitySeeder.SeedAsync( provider );

            using IServiceScope scope = provider.CreateScope();
            UserManager<WerkrUser> userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<WerkrUser>>();

            WerkrUser? operatorUser = await userManager.FindByEmailAsync("operator@werkr.local");
            Assert.IsNotNull( operatorUser );

            IList<string> roles = await userManager.GetRolesAsync(operatorUser);

            Assert.Contains( DefaultRoles.Operator.ToString( ), roles,
                "Test operator should be in Operator role." );
        } finally {
            provider.Dispose( );
        }
    }

    /// <summary>
    /// Verifies that calling <see cref="IdentitySeeder.SeedAsync"/> twice with test operator configuration enabled
    /// does not create a duplicate operator user, confirming the seeder's idempotency.
    /// </summary>
    [TestMethod]
    public async Task SeedAsync_TestOperator_SecondCallDoesNotCreateDuplicate( ) {
        ServiceProvider provider = BuildProviderWithTestOperatorConfig();
        try {
            await IdentitySeeder.SeedAsync( provider );
            await IdentitySeeder.SeedAsync( provider );

            using IServiceScope scope = provider.CreateScope();
            UserManager<WerkrUser> userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<WerkrUser>>();

            IList<WerkrUser> operators = await userManager.GetUsersInRoleAsync(
                DefaultRoles.Operator.ToString());

            Assert.HasCount( 1, operators,
                "Should have exactly one operator after double-seed." );
        } finally {
            provider.Dispose( );
        }
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> with Identity, EF Core, logging, and configuration services configured
    /// to enable test operator seeding via <c>Werkr:SeedTestOperator</c> and <c>Werkr:TestOperatorPassword</c>.
    /// </summary>
    private static ServiceProvider BuildProviderWithTestOperatorConfig( ) {
        ServiceCollection services = new();

        string dbName = $"IdentitySeederTests_{Guid.NewGuid()}";
        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            IdentityExtensions.ConfigureIdentityOptions
        )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( b => b.AddProvider( NullLoggerProvider.Instance ) );

        _ = services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder( ).AddInMemoryCollection( new Dictionary<string, string?> {
                ["Werkr:SeedTestOperator"] = "true",
                ["Werkr:TestOperatorPassword"] = "TestPassword!1Aa",
            } ).Build( )
        );

        _ = services.Configure<PasswordHistoryOptions>( _ => { } );
        _ = services.AddScoped<PasswordHistoryService>( );
        _ = services.AddSingleton<ServerConfigCache>( );

        ServiceProvider provider = services.BuildServiceProvider();

        ServerConfigCache configCache = provider.GetRequiredService<ServerConfigCache>();
        configCache.InitializeAsync( CancellationToken.None ).GetAwaiter( ).GetResult( );

        return provider;
    }
}
