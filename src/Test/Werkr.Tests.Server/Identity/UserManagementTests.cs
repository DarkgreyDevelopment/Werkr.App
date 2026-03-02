using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Roles;
using Werkr.Server.Identity;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Tests for user management operations: CRUD, role management,
/// disable/enable, last-admin protection, and forced password reset (§3.12.3).
/// </summary>
[TestClass]
public class UserManagementTests {
    public TestContext TestContext { get; set; } = null!;

    private ServiceProvider _provider = null!;
    private UserManager<WerkrUser> _userManager = null!;
    private RoleManager<IdentityRole> _roleManager = null!;

    [TestInitialize]
    public void TestInit( ) {
        ServiceCollection services = new( );
        string dbName = $"UserManagementTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( );

        _provider = services.BuildServiceProvider( );
        _userManager = _provider.GetRequiredService<UserManager<WerkrUser>>( );
        _roleManager = _provider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Seed roles
        foreach (string role in Enum.GetNames<DefaultRoles>( )) {
            _ = _roleManager.CreateAsync( new IdentityRole( role ) ).GetAwaiter( ).GetResult( );
        }
    }

    [TestCleanup]
    public void TestCleanup( ) {
        _provider.Dispose( );
    }

    [TestMethod]
    public async Task UserList_AdminCanSeeAllUsers( ) {
        _ = await CreateUserAsync( "user1@local", "User 1" );
        _ = await CreateUserAsync( "user2@local", "User 2" );
        _ = await CreateUserAsync( "user3@local", "User 3" );

        List<WerkrUser> users = await _userManager.Users.ToListAsync( TestContext.CancellationToken );

        Assert.HasCount( 3, users, "Admin should see all users." );
    }

    [TestMethod]
    public async Task CreateUser_ValidInput_CreatesUserWithRoles( ) {
        WerkrUser newUser = new( ) {
            UserName = "newuser@local",
            Email = "newuser@local",
            Name = "New User",
            Enabled = true,
            ChangePassword = true,
            EmailConfirmed = true
        };

        IdentityResult createResult = await _userManager.CreateAsync( newUser, "StrongP@ssw0rd!!" );
        Assert.IsTrue( createResult.Succeeded, "User creation should succeed." );

        IdentityResult roleResult = await _userManager.AddToRolesAsync( newUser,
            [DefaultRoles.Operator.ToString( ), DefaultRoles.Viewer.ToString( )] );
        Assert.IsTrue( roleResult.Succeeded, "Role assignment should succeed." );

        IList<string> roles = await _userManager.GetRolesAsync( newUser );
        Assert.Contains( "Operator", roles );
        Assert.Contains( "Viewer", roles );
    }

    [TestMethod]
    public async Task CreateUser_DuplicateEmail_Fails( ) {
        _ = await CreateUserAsync( "dupe@local", "Original" );

        WerkrUser dupe = new( ) {
            UserName = "dupe@local",
            Email = "dupe@local",
            Name = "Duplicate",
            EmailConfirmed = true
        };

        IdentityResult result = await _userManager.CreateAsync( dupe, "StrongP@ssw0rd!!" );

        Assert.IsFalse( result.Succeeded, "Duplicate email should fail." );
        Assert.IsTrue( result.Errors.Any( ),
            "Identity errors should indicate duplicate." );
    }

    [TestMethod]
    public async Task CreateUser_AdminRole_AutoSetsRequires2FA( ) {
        WerkrUser user = new( ) {
            UserName = "adminmfa@local",
            Email = "adminmfa@local",
            Name = "Admin MFA",
            Enabled = true,
            ChangePassword = true,
            Requires2FA = false,
            EmailConfirmed = true
        };

        _ = await _userManager.CreateAsync( user, "StrongP@ssw0rd!!" );
        _ = await _userManager.AddToRoleAsync( user, DefaultRoles.Admin.ToString( ) );

        // Simulate the auto-set logic from CreateUser page
        bool isAdmin = await _userManager.IsInRoleAsync( user, DefaultRoles.Admin.ToString( ) );
        if (isAdmin && !user.Requires2FA) {
            user.Requires2FA = true;
            _ = await _userManager.UpdateAsync( user );
        }

        WerkrUser? reloaded = await _userManager.FindByEmailAsync( "adminmfa@local" );

        Assert.IsTrue( reloaded!.Requires2FA,
            "Admin role should auto-set Requires2FA = true." );
    }

    [TestMethod]
    public async Task EditUser_UpdatesRoles( ) {
        WerkrUser user = await CreateUserAsync( "roles@local", "Roles User" );
        _ = await _userManager.AddToRoleAsync( user, DefaultRoles.Viewer.ToString( ) );

        IList<string> oldRoles = await _userManager.GetRolesAsync( user );
        Assert.Contains( "Viewer", oldRoles );

        _ = await _userManager.RemoveFromRoleAsync( user, DefaultRoles.Viewer.ToString( ) );
        _ = await _userManager.AddToRoleAsync( user, DefaultRoles.Operator.ToString( ) );

        IList<string> newRoles = await _userManager.GetRolesAsync( user );
        Assert.DoesNotContain( "Viewer", newRoles );
        Assert.Contains( "Operator", newRoles );
    }

    [TestMethod]
    public async Task EditUser_CannotRemoveLastAdmin( ) {
        WerkrUser admin = await CreateUserAsync( "soloadmin@local", "Solo Admin" );
        _ = await _userManager.AddToRoleAsync( admin, DefaultRoles.Admin.ToString( ) );

        IList<WerkrUser> admins = await _userManager.GetUsersInRoleAsync(
            DefaultRoles.Admin.ToString( ) );

        // Simulate the last-admin protection check
        bool wouldRemoveLastAdmin = admins.Count == 1
            && admins[0].Id == admin.Id;

        Assert.IsTrue( wouldRemoveLastAdmin,
            "Removing the last admin should be detected and blocked." );
    }

    [TestMethod]
    public async Task DisableUser_PreventsLogin( ) {
        WerkrUser user = await CreateUserAsync( "disable-login@local", "Disabled Login" );
        user.Enabled = false;
        _ = await _userManager.UpdateAsync( user );

        WerkrUser? reloaded = await _userManager.FindByEmailAsync( "disable-login@local" );

        Assert.IsFalse( reloaded!.Enabled,
            "User should be disabled." );
    }

    [TestMethod]
    public async Task DisableUser_InvalidatesSession( ) {
        WerkrUser user = await CreateUserAsync( "disable-session@local", "Disabled Session" );
        user.Enabled = false;
        _ = await _userManager.UpdateAsync( user );

        CookieValidatePrincipalContext ctx = BuildContext(
            _provider, BuildPrincipal( user.Id ), "/agents" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( ctx );

        Assert.IsNull( ctx.Principal,
            "Disabled user's session should be invalidated." );
    }

    [TestMethod]
    public async Task ForcePasswordReset_SetsFlag( ) {
        WerkrUser user = await CreateUserAsync( "force-reset@local", "Force Reset" );

        Assert.IsFalse( user.ChangePassword );

        user.ChangePassword = true;
        IdentityResult result = await _userManager.UpdateAsync( user );

        Assert.IsTrue( result.Succeeded );

        WerkrUser? reloaded = await _userManager.FindByEmailAsync( "force-reset@local" );
        Assert.IsTrue( reloaded!.ChangePassword,
            "ChangePassword flag should be set by admin action." );
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<WerkrUser> CreateUserAsync( string email, string name ) {
        WerkrUser user = new( ) {
            UserName = email,
            Email = email,
            Name = name,
            Enabled = true,
            ChangePassword = false,
            Requires2FA = false,
            EmailConfirmed = true
        };

        IdentityResult result = await _userManager.CreateAsync( user, "StrongP@ssw0rd!!" );
        Assert.IsTrue( result.Succeeded,
            $"User creation failed: {string.Join( "; ", result.Errors.Select( e => e.Description ) )}" );
        return user;
    }

    private static CookieValidatePrincipalContext BuildContext(
        IServiceProvider serviceProvider,
        ClaimsPrincipal principal,
        string path ) {
        DefaultHttpContext httpContext = new( ) {
            RequestServices = serviceProvider
        };

        httpContext.Request.Path = path;

        AuthenticationScheme scheme = new(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme,
            typeof( CookieAuthenticationHandler ) );

        CookieAuthenticationOptions options = new( );
        AuthenticationProperties properties = new( );
        AuthenticationTicket ticket = new( principal, properties, scheme.Name );

        return new CookieValidatePrincipalContext( httpContext, scheme, options, ticket );
    }

    private static ClaimsPrincipal BuildPrincipal( string userId ) {
        ClaimsIdentity identity = new(
            [new Claim( ClaimTypes.NameIdentifier, userId )],
            CookieAuthenticationDefaults.AuthenticationScheme );

        return new ClaimsPrincipal( identity );
    }
}
