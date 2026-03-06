using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Werkr.Common.Auth;
using Werkr.Data.Identity.Services;

namespace Werkr.Data.Identity.Authorization;

/// <summary>
/// Evaluates <see cref="PermissionRequirement"/> by checking the user's roles
/// against the role-permission mapping via <see cref="IPermissionService"/>.
/// </summary>
public sealed class PermissionAuthorizationHandler( IPermissionService permissionService )
    : AuthorizationHandler<PermissionRequirement> {

    /// <inheritdoc/>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    ) {
        if (context.User.Identity?.IsAuthenticated != true) {
            return;
        }

        IEnumerable<string> roles = context.User.FindAll( ClaimTypes.Role ).Select( c => c.Value );

        bool hasPermission = await permissionService.HasPermissionAsync( roles, requirement.Permission );

        if (hasPermission) {
            context.Succeed( requirement );
        }
    }
}
