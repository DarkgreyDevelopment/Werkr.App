using Microsoft.AspNetCore.Authorization;

namespace Werkr.Common.Auth;

/// <summary>
/// Registers the 6 standard Werkr permission-based authorization policies.
/// Each policy wraps a <see cref="PermissionRequirement"/> for a specific <see cref="Permission"/>.
/// Called by both the API and Server during startup.
/// </summary>
public static class PermissionPolicyExtensions {
    /// <summary>
    /// Adds the standard Werkr permission policies to the authorization options.
    /// Call from <c>builder.Services.AddAuthorization()</c>.
    /// </summary>
    public static AuthorizationOptions AddWerkrPermissionPolicies( this AuthorizationOptions options ) {
        options.AddPolicy(
            Policies.CanCreate,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Create ) )
        );
        options.AddPolicy(
            Policies.CanRead,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Read ) )
        );
        options.AddPolicy(
            Policies.CanUpdate,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Update ) )
        );
        options.AddPolicy(
            Policies.CanDelete,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Delete ) )
        );
        options.AddPolicy(
            Policies.CanExecute,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Execute ) )
        );
        options.AddPolicy(
            Policies.IsAdmin,
            policy => policy.Requirements.Add( new PermissionRequirement( Permission.Admin ) )
        );

        return options;
    }
}
