using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Werkr.Common.Auth;

namespace Werkr.Api.Authorization;

/// <summary>
/// Authorization handler that reads permissions from JWT claims.
/// Used by the API process where the identity DB is not available.
/// Zero DB queries per authorization check.
/// </summary>
public sealed class ClaimsPermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement> {

    /// <inheritdoc/>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    ) {
        if (context.User.Identity?.IsAuthenticated != true) {
            return Task.CompletedTask;
        }

        IEnumerable<Claim> permissionClaims = context.User.FindAll( WerkrClaimTypes.Permission );

        string requiredPermission = requirement.Permission.ToString( );

        if (permissionClaims.Any( c =>
            string.Equals( c.Value, requiredPermission, StringComparison.OrdinalIgnoreCase ) )) {
            context.Succeed( requirement );
        }

        return Task.CompletedTask;
    }
}
