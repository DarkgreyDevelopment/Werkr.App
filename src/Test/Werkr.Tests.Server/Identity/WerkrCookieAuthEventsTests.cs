using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;
using Werkr.Server.Identity;

namespace Werkr.Tests.Server.Identity;

/// <summary>
/// Unit tests for the <see cref="WerkrCookieAuthEvents"/> class defined in the <c>Werkr.Server</c> project. Validates
/// the cookie principal validation logic that handles deleted users, disabled users, forced password change redirects,
/// MFA enrollment enforcement, and the pass-through case where all user flags are nominal. Uses an in-memory <see
/// cref="WerkrIdentityDbContext"/>.
/// </summary>
[TestClass]
public class WerkrCookieAuthEventsTests {
    /// <summary>
    /// Verifies that when a cookie principal references a user ID that no longer exists in the database, the <see
    /// cref="WerkrCookieAuthEvents.ValidatePrincipal"/> method sets the context's <see cref="Principal"/> to <see
    /// langword="null"/>, effectively invalidating the session.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_UserDeleted_RejectsPrincipal( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out _ );

        ClaimsPrincipal principal = BuildPrincipal( "missing-user" );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.IsNull( context.Principal );
    }

    /// <summary>
    /// Verifies that when a cookie principal references a user whose <see cref="Enabled"/> flag is <see
    /// langword="false"/>, the <see cref="WerkrCookieAuthEvents.ValidatePrincipal"/> method sets the context's <see
    /// cref="Principal"/> to <see langword="null"/>, rejecting the disabled user's session.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_UserDisabled_RejectsPrincipal( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager );

        WerkrUser user = new( ) {
            UserName = "disabled@local",
            Email = "disabled@local",
            Name = "Disabled",
            Enabled = false,
            ChangePassword = false,
            Requires2FA = false,
            EmailConfirmed = true
        };

        _ = await userManager.CreateAsync( user, "TestPassword123!" );

        ClaimsPrincipal principal = BuildPrincipal( user.Id );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.IsNull( context.Principal );
    }

    /// <summary>
    /// Verifies that when a user has <see cref="ChangePassword"/> set to <see langword="true"/> and is navigating to a
    /// page other than "/account/change-password", the validation redirects to "/account/change-password" and marks
    /// the cookie for renewal.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_ChangePasswordTrue_RedirectsToChangePassword( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager );

        WerkrUser user = new( ) {
            UserName = "user@local",
            Email = "user@local",
            Name = "User",
            Enabled = true,
            ChangePassword = true,
            Requires2FA = false,
            EmailConfirmed = true
        };

        _ = await userManager.CreateAsync( user, "TestPassword123!" );

        ClaimsPrincipal principal = BuildPrincipal( user.Id );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/agents" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.AreEqual( "/account/change-password", context.HttpContext.Response.Headers.Location.ToString( ) );
        Assert.IsTrue( context.ShouldRenew );
    }

    /// <summary>
    /// Verifies that when a user with <see cref="ChangePassword"/> = <see langword="true"/> is already on the
    /// "/account/change-password" page, no redirect is issued (the <c>Location</c> header remains empty), allowing the
    /// user to complete the password change.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_ChangePasswordPath_DoesNotRedirect( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager );

        WerkrUser user = new( ) {
            UserName = "user2@local",
            Email = "user2@local",
            Name = "User2",
            Enabled = true,
            ChangePassword = true,
            Requires2FA = false,
            EmailConfirmed = true
        };

        _ = await userManager.CreateAsync( user, "TestPassword123!" );

        ClaimsPrincipal principal = BuildPrincipal( user.Id );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/account/change-password" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.AreEqual( string.Empty, context.HttpContext.Response.Headers.Location.ToString( ) );
    }

    /// <summary>
    /// Verifies that when a user has <see cref="Requires2FA"/> set to <see langword="true"/> but <see
    /// cref="TwoFactorEnabled"/> is <see langword="false"/>, the validation redirects to
    /// "/account/manage/mfa?required=true" and marks the cookie for renewal, enforcing the MFA enrollment requirement.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_Requires2FaNotEnrolled_RedirectsToMfa( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager );

        WerkrUser user = new( ) {
            UserName = "admin@local",
            Email = "admin@local",
            Name = "Admin",
            Enabled = true,
            ChangePassword = false,
            Requires2FA = true,
            TwoFactorEnabled = false,
            EmailConfirmed = true
        };

        _ = await userManager.CreateAsync( user, "TestPassword123!" );

        ClaimsPrincipal principal = BuildPrincipal( user.Id );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.AreEqual(
            "/account/manage/mfa?required=true",
            context.HttpContext.Response.Headers.Location.ToString( )
        );
        Assert.IsTrue( context.ShouldRenew );
    }

    /// <summary>
    /// Verifies that when all user flags are nominal (<see cref="Enabled"/> = <see langword="true"/>, <see
    /// cref="ChangePassword"/> = <see langword="false"/>, <see cref="Requires2FA"/> = <see langword="false"/>), the
    /// validation takes no action: the principal remains non-null and no redirect header is set.
    /// </summary>
    [TestMethod]
    public async Task ValidatePrincipal_AllFlagsOk_NoAction( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager );

        WerkrUser user = new( ) {
            UserName = "viewer@local",
            Email = "viewer@local",
            Name = "Viewer",
            Enabled = true,
            ChangePassword = false,
            Requires2FA = false,
            TwoFactorEnabled = false,
            EmailConfirmed = true
        };

        _ = await userManager.CreateAsync( user, "TestPassword123!" );

        ClaimsPrincipal principal = BuildPrincipal( user.Id );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.IsNotNull( context.Principal );
        Assert.AreEqual( string.Empty, context.HttpContext.Response.Headers.Location.ToString( ) );
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> configured with an in-memory <see cref="WerkrIdentityDbContext"/>,
    /// ASP.NET Core Identity services for <see cref="WerkrUser"/>, logging, and default token providers. Returns the
    /// built provider and outputs the resolved <see cref="UserManager"/> for test use.
    /// </summary>
    private static ServiceProvider BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager ) {
        ServiceCollection services = new( );
        string dbName = $"CookieEventsTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>( )
            .AddEntityFrameworkStores<WerkrIdentityDbContext>( )
            .AddDefaultTokenProviders( );

        _ = services.AddLogging( );

        ServiceProvider provider = services.BuildServiceProvider( );
        userManager = provider.GetRequiredService<UserManager<WerkrUser>>( );
        return provider;
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
