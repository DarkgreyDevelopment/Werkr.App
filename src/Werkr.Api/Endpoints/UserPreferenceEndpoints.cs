using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using Werkr.Common.Auth;
using Werkr.Data;
using Werkr.Data.Entities.Settings;

namespace Werkr.Api.Endpoints;

/// <summary>Maps per-user preference CRUD endpoints.</summary>
internal static class UserPreferenceEndpoints {

    /// <summary>Maps user preference endpoints at <c>/api/v1/user/preferences</c>.</summary>
    public static WebApplication MapUserPreferenceEndpoints( this WebApplication app ) {

        // GET /api/v1/user/preferences — list all preferences for the authenticated user
        _ = app.MapGet( "/api/v1/user/preferences", async (
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            List<PreferenceDto> prefs = await dbContext.UserPreferences
                .AsNoTracking( )
                .Where( p => p.UserId == userId )
                .Select( p => new PreferenceDto( p.Key, p.Value ) )
                .ToListAsync( ct );

            return Results.Ok( prefs );
        } )
        .RequireAuthorization( );

        // PUT /api/v1/user/preferences/{key} — upsert a single preference
        _ = app.MapPut( "/api/v1/user/preferences/{key}", async (
            string key,
            SetPreferenceRequest request,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            if (string.IsNullOrWhiteSpace( key ) || key.Length > 100) {
                return Results.BadRequest( new { message = "Key must be between 1 and 100 characters." } );
            }

            if (string.IsNullOrWhiteSpace( request.Value ) || request.Value.Length > 500) {
                return Results.BadRequest( new { message = "Value must be between 1 and 500 characters." } );
            }

            // Key-specific validation
            if (string.Equals( key, "DisplayTimeZoneId", StringComparison.OrdinalIgnoreCase )) {
                try {
                    _ = TimeZoneResolver.FindOrCreate( request.Value );
                } catch (TimeZoneNotFoundException) {
                    return Results.BadRequest( new { message = $"'{request.Value}' is not a valid timezone identifier." } );
                }
            }

            UserPreference? existing = await dbContext.UserPreferences
                .FirstOrDefaultAsync( p => p.UserId == userId && p.Key == key, ct );

            if (existing is not null) {
                existing.Value = request.Value;
                existing.LastUpdated = DateTime.UtcNow;
                existing.Version++;
            } else {
                DateTime now = DateTime.UtcNow;
                _ = dbContext.UserPreferences.Add( new UserPreference {
                    UserId = userId,
                    Key = key,
                    Value = request.Value,
                    Created = now,
                    LastUpdated = now,
                    Version = 1,
                } );
            }

            _ = await dbContext.SaveChangesAsync( ct );
            return Results.Ok( new PreferenceDto( key, request.Value ) );
        } )
        .RequireAuthorization( );

        // DELETE /api/v1/user/preferences/{key} — delete a single preference
        _ = app.MapDelete( "/api/v1/user/preferences/{key}", async (
            string key,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            UserPreference? entity = await dbContext.UserPreferences
                .FirstOrDefaultAsync( p => p.UserId == userId && p.Key == key, ct );

            if (entity is null) {
                return Results.NotFound( );
            }

            _ = dbContext.UserPreferences.Remove( entity );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.NoContent( );
        } )
        .RequireAuthorization( );

        // DELETE /api/v1/user/{userId}/preferences — admin bulk delete for orphan cleanup
        _ = app.MapDelete( "/api/v1/user/{userId}/preferences", async (
            string userId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            List<UserPreference> prefs = await dbContext.UserPreferences
                .Where( p => p.UserId == userId )
                .ToListAsync( ct );

            if (prefs.Count == 0) {
                return Results.NoContent( );
            }

            dbContext.UserPreferences.RemoveRange( prefs );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.NoContent( );
        } )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}

/// <summary>Request body for setting a preference value.</summary>
internal sealed record SetPreferenceRequest( string Value );

/// <summary>Response DTO for a single preference.</summary>
internal sealed record PreferenceDto( string Key, string Value );
