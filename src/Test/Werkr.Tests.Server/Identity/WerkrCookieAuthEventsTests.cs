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

[TestClass]
public class WerkrCookieAuthEventsTests {
    [TestMethod]
    public async Task ValidatePrincipal_UserDeleted_RejectsPrincipal( ) {
        ServiceProvider serviceProvider = BuildIdentityServiceProvider( out _ );

        ClaimsPrincipal principal = BuildPrincipal( "missing-user" );
        CookieValidatePrincipalContext context = BuildContext( serviceProvider, principal, "/" );

        WerkrCookieAuthEvents events = new( );
        await events.ValidatePrincipal( context );

        Assert.IsNull( context.Principal );
    }

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

        Assert.AreEqual( "/account/manage/mfa?required=true", context.HttpContext.Response.Headers.Location.ToString( ) );
        Assert.IsTrue( context.ShouldRenew );
    }

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
