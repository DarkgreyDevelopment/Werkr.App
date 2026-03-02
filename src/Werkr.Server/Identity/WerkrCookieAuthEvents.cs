using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

using Werkr.Data.Identity.Entities;

namespace Werkr.Server.Identity;

/// <summary>
/// Cookie authentication event handlers for Werkr-specific security policies.
/// </summary>
public sealed class WerkrCookieAuthEvents : CookieAuthenticationEvents {
    /// <inheritdoc />
    public override async Task ValidatePrincipal( CookieValidatePrincipalContext context ) {
        if (context.Principal?.Identity?.IsAuthenticated != true) {
            return;
        }

        UserManager<WerkrUser> userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<WerkrUser>>( );

        WerkrUser? user = await userManager.GetUserAsync( context.Principal );
        if (user is null) {
            context.RejectPrincipal( );
            return;
        }

        PathString requestPath = context.HttpContext.Request.Path;

        if (!user.Enabled) {
            context.RejectPrincipal( );
            await context.HttpContext.SignOutAsync( IdentityConstants.ApplicationScheme );
            return;
        }

        if (user.ChangePassword && !IsAllowedPathForPasswordChange( requestPath )) {
            context.HttpContext.Response.Redirect( "/account/change-password" );
            context.ShouldRenew = true;
            return;
        }

        if (user.Requires2FA && !user.TwoFactorEnabled && !IsAllowedPathForMfaEnrollment( requestPath )) {
            context.HttpContext.Response.Redirect( "/account/manage/mfa?required=true" );
            context.ShouldRenew = true;
        }
    }

    private static bool IsAllowedPathForPasswordChange( PathString path ) {
        return path.StartsWithSegments( "/account/change-password", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/_blazor", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/account/logout", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/account/access-denied", StringComparison.OrdinalIgnoreCase )
            || IsStaticAsset( path );
    }

    private static bool IsAllowedPathForMfaEnrollment( PathString path ) {
        return path.StartsWithSegments( "/account/manage/mfa", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/_blazor", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/account/logout", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/account/access-denied", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/account/change-password", StringComparison.OrdinalIgnoreCase )
            || IsStaticAsset( path );
    }

    private static bool IsStaticAsset( PathString path ) {
        return path.StartsWithSegments( "/_framework", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/_content", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/lib", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/css", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/js", StringComparison.OrdinalIgnoreCase )
            || path.StartsWithSegments( "/images", StringComparison.OrdinalIgnoreCase ) || Path.HasExtension( path.Value );
    }
}
