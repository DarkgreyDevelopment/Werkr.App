using System.Security.Claims;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Data.Identity.Entities;
using Werkr.Data.Identity.Services;
using Werkr.Server.Identity;

namespace Werkr.Server.Endpoints;

/// <summary>
/// Maps authentication and API key management endpoints on the Server.
/// The Server is the sole JWT token issuer (Decision A1).
/// </summary>
public static class AuthEndpoints {
    /// <summary>
    /// Maps auth-related endpoints: token exchange and API key CRUD.
    /// </summary>
    public static WebApplication MapAuthEndpoints( this WebApplication app ) {
        // ── Token Exchange (unauthenticated) ──

        _ = app.MapPost( "/api/v1/auth/token", async (
            TokenRequest request,
            ApiKeyService apiKeyService,
            JwtTokenService tokenService,
            IPermissionService permissionService,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.ApiKey )) {
                return Results.BadRequest( new { message = "API key is required." } );
            }

            ApiKey? apiKey = await apiKeyService.ValidateAsync( request.ApiKey, ct );
            if (apiKey is null) {
                return Results.Unauthorized( );
            }

            IReadOnlyList<Permission> permissions =
                    await permissionService.GetPermissionsForRoleAsync( apiKey.Role, ct );
            string token = tokenService.GenerateToken( apiKey, permissions );
            DateTime expiresUtc = DateTime.UtcNow.AddMinutes( 15 );
            return Results.Ok( new TokenResponse( token, expiresUtc ) );
        } )
        .WithName( "ExchangeApiKeyForToken" )
        .WithTags( "Auth" )
        .AllowAnonymous( );

        // ── API Key Management ──

        _ = app.MapPost( "/api/v1/auth/keys", async (
            ApiKeyCreateRequest request,
            ApiKeyService apiKeyService,
            ClaimsPrincipal user,
            CancellationToken ct
        ) => {
            string? userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            string? userRole = user.FindFirst( ClaimTypes.Role )?.Value;
            if (string.IsNullOrWhiteSpace( userRole )) {
                return Results.Forbid( );
            }

            (ApiKey apiKey, string rawKey) = await apiKeyService.CreateAsync(
                request.Name, userRole, userId, request.ExpiresUtc, ct
            );

            return Results.Created( $"/api/v1/auth/keys/{apiKey.Id}", new ApiKeyCreateResponse(
                    apiKey.Id, apiKey.Name, rawKey, apiKey.KeyPrefix, apiKey.Role,
                    apiKey.CreatedUtc, apiKey.ExpiresUtc
                ) );
        } )
        .WithName( "CreateApiKey" )
        .WithTags( "Auth" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/v1/auth/keys", async (
            ApiKeyService apiKeyService,
            CancellationToken ct
        ) => {
            IReadOnlyList<ApiKey> keys = await apiKeyService.GetAllAsync( ct );
            List<ApiKeyDto> dtos = [.. keys.Select( k => new ApiKeyDto(
                    k.Id, k.Name, k.KeyPrefix, k.Role, k.CreatedByUserId,
                    k.CreatedUtc, k.ExpiresUtc, k.IsRevoked, k.LastUsedUtc
                ) )];
            return Results.Ok( dtos );
        } )
        .WithName( "ListApiKeys" )
        .WithTags( "Auth" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapDelete( "/api/v1/auth/keys/{id}", async (
            Guid id,
            ApiKeyService apiKeyService,
            CancellationToken ct
        ) => {
            bool revoked = await apiKeyService.RevokeAsync( id, ct );
            return revoked ? Results.NoContent( ) : Results.NotFound( );
        } )
        .WithName( "RevokeApiKey" )
        .WithTags( "Auth" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
