using Werkr.Common.Auth;

namespace Werkr.Data.Identity.Services;

/// <summary>
/// Service for checking user permissions against the role-permission mapping.
/// </summary>
public interface IPermissionService {
    /// <summary>
    /// Checks whether any of the specified roles has the given permission.
    /// </summary>
    /// <param name="roles">The role names to check.</param>
    /// <param name="permission">The permission to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if at least one role has the permission; otherwise <see langword="false"/>.</returns>
    Task<bool> HasPermissionAsync( IEnumerable<string> roles, Permission permission, CancellationToken ct = default );

    /// <summary>
    /// Gets all permissions granted to any of the specified roles.
    /// </summary>
    /// <param name="roles">The role names to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Distinct set of permissions.</returns>
    Task<IReadOnlySet<Permission>> GetPermissionsAsync( IEnumerable<string> roles, CancellationToken ct = default );

    /// <summary>
    /// Gets the list of permissions granted to a specific role by name.
    /// Used by the token issuer to embed permissions in JWT claims.
    /// </summary>
    /// <param name="roleName">The role name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of granted permissions.</returns>
    Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync( string roleName, CancellationToken ct = default );
}
