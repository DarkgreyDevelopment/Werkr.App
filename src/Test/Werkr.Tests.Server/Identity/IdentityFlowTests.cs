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
/// Tests for the forced password change, MFA enrollment, MFA verification,
/// and admin MFA policy enforcement flows.
/// </summary>
[TestClass]
public class IdentityFlowTests {
    /// <summary>
    /// Verifies that a user with the <see cref="ChangePassword"/> flag set to <see langword="true"/> is redirected to
    /// the "/account/change-password" page when the <see cref="WerkrCookieAuthEvents"/> validates the cookie
    /// principal, regardless of the originally requested path.
    /// </summary>
    [TestMethod]
    public async Task ForcedPasswordChange_RedirectsToChangePassword( ) {
        ServiceProvider sp = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "force@local", changePassword: true, enabled: true );

        CookieValidatePrincipalContext ctx = BuildContext( sp, BuildPrincipal( user.Id ), "/" );
        await new WerkrCookieAuthEvents( ).ValidatePrincipal( ctx );

        Assert.AreEqual( "/account/change-password",
            ctx.HttpContext.Response.Headers.Location.ToString( ) );
    }

    /// <summary>
    /// Verifies that a user with the <see cref="ChangePassword"/> flag set to <see langword="true"/> who attempts to
    /// navigate to a page other than "/account/change-password" (e.g., "/agents") is still redirected to the change
    /// password page and that the cookie is marked for renewal.
    /// </summary>
    [TestMethod]
    public async Task ForcedPasswordChange_CannotAccessOtherPages( ) {
        ServiceProvider sp = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "force2@local", changePassword: true, enabled: true );

        CookieValidatePrincipalContext ctx = BuildContext( sp, BuildPrincipal( user.Id ), "/agents" );
        await new WerkrCookieAuthEvents( ).ValidatePrincipal( ctx );

        Assert.AreEqual( "/account/change-password",
            ctx.HttpContext.Response.Headers.Location.ToString( ) );
        Assert.IsTrue( ctx.ShouldRenew );
    }

    /// <summary>
    /// Verifies that a successful password change via <see cref="UserManager.ChangePasswordAsync"/> allows the <see
    /// cref="ChangePassword"/> flag on the <see cref="WerkrUser"/> to be cleared, confirming that the forced password
    /// reset workflow can complete end-to-end.
    /// </summary>
    [TestMethod]
    public async Task ChangePassword_SuccessfulChange_ClearsFlag( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "pwd-clear@local", changePassword: true, enabled: true );

        // Simulate a successful password change
        IdentityResult result = await um.ChangePasswordAsync( user, "TestPassword123!", "NewStrongP@$$w0rd" );

        Assert.IsTrue( result.Succeeded, "Password change should succeed." );

        user.ChangePassword = false;
        _ = await um.UpdateAsync( user );

        WerkrUser? reloaded = await um.FindByEmailAsync( "pwd-clear@local" );
        Assert.IsFalse( reloaded!.ChangePassword,
            "ChangePassword flag should be cleared after successful change." );
    }

    /// <summary>
    /// Verifies that attempting to change a password to a value that does not meet the identity password policy (e.g.,
    /// too short) fails with <see cref="IdentityResult.Succeeded"/> equal to <see langword="false"/> and returns one
    /// or more error descriptions.
    /// </summary>
    [TestMethod]
    public async Task ChangePassword_WeakPassword_ShowsError( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "pwd-weak@local", changePassword: true, enabled: true );

        // Attempt with a weak password that violates OWASP policy (< 12 chars, no special)
        IdentityResult result = await um.ChangePasswordAsync( user, "TestPassword123!", "short" );

        Assert.IsFalse( result.Succeeded,
            "Password change with weak password should fail." );
        Assert.IsTrue( result.Errors.Any( ),
            "Identity errors should be returned for policy violation." );
    }

    /// <summary>
    /// Verifies that the MFA enrollment flow can generate an authenticator key for a user via <see
    /// cref="UserManager.GetAuthenticatorKeyAsync"/> (resetting first if necessary), producing a non-null,
    /// non-whitespace key string.
    /// </summary>
    [TestMethod]
    public async Task MfaEnrollment_GeneratesAuthenticatorKey( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "mfa-gen@local", enabled: true );

        string? key = await um.GetAuthenticatorKeyAsync( user );
        if (string.IsNullOrWhiteSpace( key )) {
            _ = await um.ResetAuthenticatorKeyAsync( user );
            key = await um.GetAuthenticatorKeyAsync( user );
        }

        Assert.IsFalse( string.IsNullOrWhiteSpace( key ),
            "Authenticator key should be generated for MFA enrollment." );
    }

    /// <summary>
    /// Verifies that calling <see cref="UserManager.SetTwoFactorEnabledAsync"/> with <see langword="true"/> succeeds
    /// and that <see cref="GetTwoFactorEnabledAsync"/> subsequently returns <see langword="true"/>, simulating the
    /// final step of MFA enrollment after TOTP code validation.
    /// </summary>
    [TestMethod]
    public async Task MfaEnrollment_ValidCode_EnablesTwoFactor( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "mfa-enable@local", enabled: true );

        Assert.IsFalse( await um.GetTwoFactorEnabledAsync( user ) );

        IdentityResult result = await um.SetTwoFactorEnabledAsync( user, true );

        Assert.IsTrue( result.Succeeded, "Enabling 2FA should succeed." );
        Assert.IsTrue( await um.GetTwoFactorEnabledAsync( user ),
            "TwoFactorEnabled should be true after enrollment." );
    }

    /// <summary>
    /// Verifies that <see cref="UserManager.VerifyTwoFactorTokenAsync"/> returns <see langword="false"/> when given an
    /// invalid TOTP code ("000000"), confirming that MFA verification rejects bad codes.
    /// </summary>
    [TestMethod]
    public async Task MfaVerify_InvalidCode_FailsVerification( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "mfa-invalid@local", enabled: true );

        _ = await um.ResetAuthenticatorKeyAsync( user );

        bool isValid = await um.VerifyTwoFactorTokenAsync(
            user,
            um.Options.Tokens.AuthenticatorTokenProvider,
            "000000" );

        Assert.IsFalse( isValid, "Invalid TOTP code should fail verification." );
    }

    /// <summary>
    /// Verifies that redeeming a two-factor recovery code via <see
    /// cref="UserManager.RedeemTwoFactorRecoveryCodeAsync"/> succeeds and decrements the remaining recovery code count
    /// by exactly one.
    /// </summary>
    [TestMethod]
    public async Task MfaVerify_RecoveryCode_ConsumesCode( ) {
        _ = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "mfa-recovery@local", enabled: true );

        _ = await um.SetTwoFactorEnabledAsync( user, true );

        IEnumerable<string>? codes = await um.GenerateNewTwoFactorRecoveryCodesAsync( user, 5 );
        Assert.IsNotNull( codes );

        string firstCode = codes.First( );

        int countBefore = await um.CountRecoveryCodesAsync( user );
        IdentityResult redeemResult = await um.RedeemTwoFactorRecoveryCodeAsync( user, firstCode );
        int countAfter = await um.CountRecoveryCodesAsync( user );

        Assert.IsTrue( redeemResult.Succeeded, "Recovery code redemption should succeed." );
        Assert.AreEqual( countBefore - 1, countAfter,
            "Recovery code count should decrease after use." );
    }

    /// <summary>
    /// Verifies that a user with <see cref="Requires2FA"/> set to <see langword="true"/> but <see
    /// cref="TwoFactorEnabled"/> set to <see langword="false"/> is redirected to "/account/manage/mfa?required=true"
    /// during cookie principal validation, enforcing the admin MFA enrollment policy.
    /// </summary>
    [TestMethod]
    public async Task AdminMfaPolicy_EnforcesEnrollment( ) {
        ServiceProvider sp = BuildIdentityServiceProvider( out UserManager<WerkrUser> um );

        WerkrUser user = await CreateUserAsync( um,
            "admin-mfa@local", enabled: true, requires2FA: true );

        CookieValidatePrincipalContext ctx = BuildContext( sp, BuildPrincipal( user.Id ), "/" );
        await new WerkrCookieAuthEvents( ).ValidatePrincipal( ctx );

        Assert.AreEqual( "/account/manage/mfa?required=true",
            ctx.HttpContext.Response.Headers.Location.ToString( ),
            "Admin with Requires2FA but no TwoFactorEnabled should be redirected to MFA enrollment." );
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WerkrUser"/> with the specified email, enabled state, change-password flag, and MFA
    /// requirement flag using the provided <see cref="UserManager"/>. The user is assigned a default password of
    /// "TestPassword123!" and has email confirmation pre-set.
    /// </summary>
    private static async Task<WerkrUser> CreateUserAsync(
        UserManager<WerkrUser> um,
        string email,
        bool enabled = true,
        bool changePassword = false,
        bool requires2FA = false
    ) {
        WerkrUser user = new( ) {
            UserName = email,
            Email = email,
            Name = email.Split( '@' )[0],
            Enabled = enabled,
            ChangePassword = changePassword,
            Requires2FA = requires2FA,
            TwoFactorEnabled = false,
            EmailConfirmed = true
        };

        IdentityResult result = await um.CreateAsync( user, "TestPassword123!" );
        Assert.IsTrue(
            result.Succeeded,
            "User creation failed: " +
                string.Join( "; ", result.Errors.Select( e => e.Description ) )
        );
        return user;
    }

    /// <summary>
    /// Builds a <see cref="ServiceProvider"/> configured with an in-memory <see cref="WerkrIdentityDbContext"/>,
    /// ASP.NET Core Identity services for <see cref="WerkrUser"/>, and default token providers. Returns the built
    /// provider and outputs the resolved <see cref="UserManager"/> for test use.
    /// </summary>
    private static ServiceProvider BuildIdentityServiceProvider( out UserManager<WerkrUser> userManager ) {
        ServiceCollection services = new( );
        string dbName = $"IdentityFlowTests_{Guid.NewGuid( )}";

        _ = services.AddDbContext<WerkrIdentityDbContext>( options =>
            options.UseInMemoryDatabase( dbName ) );

        _ = services.AddIdentity<WerkrUser, IdentityRole>(
            Werkr.Data.Identity.Extensions.IdentityExtensions.ConfigureIdentityOptions
        )
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
