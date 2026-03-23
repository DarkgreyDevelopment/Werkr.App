using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Werkr.Common.Auth;
using Werkr.Data.Identity.Services;

namespace Werkr.Server.Identity;

/// <summary>
/// Scoped service that resolves the current Blazor circuit user's claims
/// and mints a short-lived JWT carrying their real identity, roles, and
/// DB-backed permissions.
/// </summary>
public sealed class BlazorUserTokenProvider(
    AuthenticationStateProvider authStateProvider,
    IPermissionService permissionService,
    JwtTokenService tokenService
) : IUserTokenProvider {
    /// <inheritdoc/>
    public async Task<string?> GetTokenAsync( ) {
        AuthenticationState authState = await authStateProvider.GetAuthenticationStateAsync( );
        ClaimsPrincipal user = authState.User;

        if (user.Identity is not { IsAuthenticated: true }) {
            return null;
        }

        string? userId = user.FindFirstValue( ClaimTypes.NameIdentifier );
        string? userName = user.FindFirstValue( ClaimTypes.Name );

        if (string.IsNullOrEmpty( userId )) {
            return null;
        }

        IEnumerable<string> roles = user.Claims
            .Where( c => c.Type == ClaimTypes.Role )
            .Select( c => c.Value );

        IReadOnlySet<Permission> permissions = await permissionService.GetPermissionsAsync( roles );

        return tokenService.GenerateUserToken( userId, userName ?? userId, roles, permissions );
    }
}
