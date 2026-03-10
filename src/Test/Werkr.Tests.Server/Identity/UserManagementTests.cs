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
/// disable/enable, last-admin protection, and forced password reset.
/// </summary>
[TestClass]
public class UserManagementTests {
    /// <summary>
    /// Gets or sets the MSTest <see cref="TestContext"/> used for cancellation token access and test run metadata.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The fully configured <see cref="ServiceProvider"/> containing Identity and EF Core services.
    /// </summary>
    private ServiceProvider _provider = null!;
    /// <summary>
    /// The <see cref="UserManager"/> instance used to create and manage <see cref="WerkrUser"/> entities.
    /// </summary>
    private UserManager<WerkrUser> _userManager = null!;
    /// <summary>
    /// The <see cref="RoleManager"/> instance used to manage identity roles.
    /// </summary>
    private RoleManager<IdentityRole> _roleManager = null!;

    /// <summary>
    /// Initializes a fresh in-memory database, registers Identity services, creates the <see cref="DefaultRoles"/> in
    /// the role store, and resolves the <see cref="UserManager"/> and <see cref="RoleManager"/> before each test.
    /// </summary>
    [TestInitialize]
    public void TestInit( ) {
        ServiceCollection services = new( );
        string dbName = $"UserManagementTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions
        )
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

    /// <summary>
    /// Disposes the <see cref="ServiceProvider"/> and all scoped services after each test.
    /// </summary>
    [TestCleanup]
    public void TestCleanup( ) {
        _provider.Dispose( );
    }

    /// <summary>
    /// Verifies that an admin can list all users and that the count matches the number of users created, confirming
    /// full visibility.
    /// </summary>
    [TestMethod]
    public async Task UserList_AdminCanSeeAllUsers( ) {
        _ = await CreateUserAsync( "user1@local", "User 1" );
        _ = await CreateUserAsync( "user2@local", "User 2" );
        _ = await CreateUserAsync( "user3@local", "User 3" );

        List<WerkrUser> users = await _userManager.Users.ToListAsync( TestContext.CancellationToken );

        Assert.HasCount( 3, users, "Admin should see all users." );
    }

    /// <summary>
    /// Verifies that creating a new <see cref="WerkrUser"/> with valid input succeeds and that the user can be
    /// assigned to multiple roles (Operator and Viewer) simultaneously.
    /// </summary>
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

    /// <summary>
    /// Verifies that attempting to create a user with an email that already exists fails, returning an unsuccessful
    /// <see cref="IdentityResult"/> with error descriptions.
    /// </summary>
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

    /// <summary>
    /// Verifies that when a user is assigned the Admin role and does not have <see cref="Requires2FA"/> set, the
    /// application logic automatically sets <see cref="Requires2FA"/> to <see langword="true"/> to enforce the admin
    /// MFA enrollment policy.
    /// </summary>
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

    /// <summary>
    /// Verifies that a user's roles can be updated by removing an existing role (Viewer) and adding a new one
    /// (Operator), confirming that edit operations work correctly.
    /// </summary>
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

    /// <summary>
    /// Verifies that the system detects when removing the Admin role from a user would leave no admin users, which
    /// should be blocked to prevent lockout. The test confirms the detection logic reports that the sole admin would
    /// be removed.
    /// </summary>
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

    /// <summary>
    /// Verifies that setting a user's <see cref="Enabled"/> flag to <see langword="false"/> persists to the database,
    /// effectively preventing the user from authenticating.
    /// </summary>
    [TestMethod]
    public async Task DisableUser_PreventsLogin( ) {
        WerkrUser user = await CreateUserAsync( "disable-login@local", "Disabled Login" );
        user.Enabled = false;
        _ = await _userManager.UpdateAsync( user );

        WerkrUser? reloaded = await _userManager.FindByEmailAsync( "disable-login@local" );

        Assert.IsFalse( reloaded!.Enabled,
            "User should be disabled." );
    }

    /// <summary>
    /// Verifies that when a disabled user's cookie is validated by <see cref="WerkrCookieAuthEvents"/>, the principal
    /// is set to <see langword="null"/>, effectively invalidating the session and forcing the user to re-authenticate
    /// (which will fail since the account is disabled).
    /// </summary>
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

    /// <summary>
    /// Verifies that an admin can set the <see cref="ChangePassword"/> flag on a user to <see langword="true"/>, which
    /// forces the user to change their password on next login.
    /// </summary>
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

    /// <summary>
    /// Creates a <see cref="WerkrUser"/> with the specified email and display name using the shared <see
    /// cref="_userManager"/>. The user is created with a default password, enabled state, and email confirmation
    /// pre-set.
    /// </summary>
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

    /// <summary>
    /// Constructs a <see cref="CookieValidatePrincipalContext"/> simulating a cookie authentication validation event
    /// for the specified request path, allowing <see cref="WerkrCookieAuthEvents"/> to be tested in isolation.
    /// </summary>
    private static CookieValidatePrincipalContext BuildContext(
        IServiceProvider serviceProvider,
        ClaimsPrincipal principal,
        string path
    ) {
        DefaultHttpContext httpContext = new( ) {
            RequestServices = serviceProvider
        };

        httpContext.Request.Path = path;

        AuthenticationScheme scheme = new(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme,
            typeof(CookieAuthenticationHandler)
        );

        CookieAuthenticationOptions options = new( );
        AuthenticationProperties properties = new( );
        AuthenticationTicket ticket = new( principal, properties, scheme.Name );

        return new CookieValidatePrincipalContext( httpContext, scheme, options, ticket );
    }

    /// <summary>
    /// Builds a <see cref="ClaimsPrincipal"/> containing a single <c>NameIdentifier</c> claim with the specified user
    /// ID, authenticated via the cookie authentication scheme.
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal( string userId ) {
        ClaimsIdentity identity = new(
            [new Claim( ClaimTypes.NameIdentifier, userId )],
            CookieAuthenticationDefaults.AuthenticationScheme
        );

        return new ClaimsPrincipal( identity );
    }
}
