using Microsoft.AspNetCore.Identity;
using Werkr.Common.Auth;
using Werkr.Data.Identity.Entities;

namespace Werkr.Data.Identity.Services;

/// <summary>
/// Checks permissions by querying the <see cref="RolePermission"/> table
/// against the current user's roles.
/// </summary>
public sealed class PermissionService( WerkrIdentityDbContext dbContext, RoleManager<IdentityRole> roleManager )
    : IPermissionService {

    /// <inheritdoc/>
    public async Task<bool> HasPermissionAsync(
        IEnumerable<string> roles,
        Permission permission,
        CancellationToken ct = default
    ) {
        List<string> roleIds = await GetRoleIdsAsync( roles, ct );
        return roleIds.Count != 0 && await dbContext.RolePermissions
            .AnyAsync( rp => roleIds.Contains( rp.RoleId ) && rp.Permission == permission, ct );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<Permission>> GetPermissionsAsync(
        IEnumerable<string> roles,
        CancellationToken ct = default
    ) {
        List<string> roleIds = await GetRoleIdsAsync( roles, ct );
        if (roleIds.Count == 0) {
            return new HashSet<Permission>( );
        }

        List<Permission> permissions = await dbContext.RolePermissions
            .Where( rp => roleIds.Contains( rp.RoleId ) )
            .Select( rp => rp.Permission )
            .Distinct( )
            .ToListAsync( ct );

        return permissions.ToHashSet( );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Permission>> GetPermissionsForRoleAsync(
        string roleName,
        CancellationToken ct = default
    ) {
        IdentityRole? role = await roleManager.FindByNameAsync( roleName );
        if (role is null) {
            return [];
        }

        List<Permission> permissions = await dbContext.RolePermissions
            .Where( rp => rp.RoleId == role.Id )
            .Select( rp => rp.Permission )
            .Distinct( )
            .ToListAsync( ct );

        return permissions;
    }

    /// <summary>
    /// Resolves a collection of role names to their corresponding identity role ID values by querying the <see cref="RoleManager{TRole}"/>.
    /// </summary>
    private async Task<List<string>> GetRoleIdsAsync( IEnumerable<string> roleNames, CancellationToken ct ) {
        List<string> roleIds = [];
        foreach (string roleName in roleNames) {
            if (ct.IsCancellationRequested) { break; }
            IdentityRole? role = await roleManager.FindByNameAsync( roleName );
            if (role is not null) {
                roleIds.Add( role.Id );
            }
        }
        return roleIds;
    }
}
