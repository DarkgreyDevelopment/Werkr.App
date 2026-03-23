using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Data;
using Werkr.Data.Entities.Settings;

namespace Werkr.Api.Endpoints;

/// <summary>Maps CRUD endpoints for server-synced saved filters.</summary>
internal static class FilterEndpoints {

    private static readonly HashSet<string> s_validPageKeys = [
        "runs", "workflows", "jobs", "agents", "schedules", "tasks",
        "all-workflow-runs", "workflow-dashboard"
    ];

    /// <summary>Maps the saved-filter endpoints at <c>/api/filters/{pageKey}</c>.</summary>
    public static WebApplication MapFilterEndpoints( this WebApplication app ) {

        // GET /api/filters/{pageKey} — list own + shared filters
        _ = app.MapGet( "/api/v1/filters/{pageKey}", async (
            string pageKey,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            if (!s_validPageKeys.Contains( pageKey )) {
                return Results.BadRequest( new { message = $"Invalid page key: {pageKey}" } );
            }

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            List<SavedFilter> filters = await dbContext.SavedFilters
                .AsNoTracking( )
                .Where( f => f.PageKey == pageKey && ( f.OwnerId == userId || f.IsShared ) )
                .OrderBy( f => f.Name )
                .ToListAsync( ct );

            List<object> dtos = [.. filters.Select( f => new {
                f.Id,
                f.Name,
                f.PageKey,
                f.CriteriaJson,
                f.IsShared,
                IsOwner = f.OwnerId == userId
            } )];

            return Results.Ok( dtos );
        } )
        .RequireAuthorization( Policies.CanRead );

        // POST /api/filters/{pageKey} — create a filter
        _ = app.MapPost( "/api/v1/filters/{pageKey}", async (
            string pageKey,
            CreateFilterRequest request,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            if (!s_validPageKeys.Contains( pageKey )) {
                return Results.BadRequest( new { message = $"Invalid page key: {pageKey}" } );
            }

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            if (string.IsNullOrWhiteSpace( request.Name )) {
                return Results.BadRequest( new { message = "Name is required." } );
            }

            if (!IsValidJson( request.CriteriaJson )) {
                return Results.BadRequest( new { message = "CriteriaJson must be valid JSON." } );
            }

            if (request.CriteriaJson.Length > 4096) {
                return Results.BadRequest( new { message = "CriteriaJson must not exceed 4 KB." } );
            }

            DateTime now = DateTime.UtcNow;
            SavedFilter entity = new( ) {
                OwnerId = userId,
                PageKey = pageKey,
                Name = request.Name.Trim( ),
                CriteriaJson = request.CriteriaJson,
                IsShared = false,
                Created = now,
                LastUpdated = now,
                Version = 1,
            };

            _ = dbContext.SavedFilters.Add( entity );
            _ = await dbContext.SaveChangesAsync( ct );

            return Results.Created( $"/api/v1/filters/{pageKey}/{entity.Id}", new {
                entity.Id,
                entity.Name,
                entity.PageKey,
                entity.CriteriaJson,
                entity.IsShared,
                IsOwner = true
            } );
        } )
        .RequireAuthorization( Policies.CanCreate );

        // PUT /api/filters/{pageKey}/{id} — update own filter
        _ = app.MapPut( "/api/v1/filters/{pageKey}/{id}", async (
            string pageKey,
            long id,
            UpdateFilterRequest request,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            if (!s_validPageKeys.Contains( pageKey )) {
                return Results.BadRequest( new { message = $"Invalid page key: {pageKey}" } );
            }

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            SavedFilter? entity = await dbContext.SavedFilters
                .FirstOrDefaultAsync( f => f.Id == id && f.PageKey == pageKey, ct );

            if (entity is null) {
                return Results.NotFound( );
            }

            if (entity.OwnerId != userId) {
                return Results.Forbid( );
            }

            if (!string.IsNullOrWhiteSpace( request.Name )) {
                entity.Name = request.Name.Trim( );
            }

            if (request.CriteriaJson is not null) {
                if (!IsValidJson( request.CriteriaJson )) {
                    return Results.BadRequest( new { message = "CriteriaJson must be valid JSON." } );
                }
                if (request.CriteriaJson.Length > 4096) {
                    return Results.BadRequest( new { message = "CriteriaJson must not exceed 4 KB." } );
                }
                entity.CriteriaJson = request.CriteriaJson;
            }

            entity.LastUpdated = DateTime.UtcNow;
            entity.Version++;
            _ = await dbContext.SaveChangesAsync( ct );

            return Results.Ok( new {
                entity.Id,
                entity.Name,
                entity.PageKey,
                entity.CriteriaJson,
                entity.IsShared,
                IsOwner = true
            } );
        } )
        .RequireAuthorization( Policies.CanUpdate );

        // DELETE /api/filters/{pageKey}/{id} — delete own filter
        _ = app.MapDelete( "/api/v1/filters/{pageKey}/{id}", async (
            string pageKey,
            long id,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            if (!s_validPageKeys.Contains( pageKey )) {
                return Results.BadRequest( new { message = $"Invalid page key: {pageKey}" } );
            }

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            if (string.IsNullOrWhiteSpace( userId )) {
                return Results.Unauthorized( );
            }

            SavedFilter? entity = await dbContext.SavedFilters
                .FirstOrDefaultAsync( f => f.Id == id && f.PageKey == pageKey, ct );

            if (entity is null) {
                return Results.NotFound( );
            }

            if (entity.OwnerId != userId) {
                return Results.Forbid( );
            }

            _ = dbContext.SavedFilters.Remove( entity );
            _ = await dbContext.SaveChangesAsync( ct );

            return Results.NoContent( );
        } )
        .RequireAuthorization( Policies.CanDelete );

        // PUT /api/filters/{pageKey}/{id}/share — toggle shared visibility (admin only)
        _ = app.MapPut( "/api/v1/filters/{pageKey}/{id}/share", async (
            string pageKey,
            long id,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            if (!s_validPageKeys.Contains( pageKey )) {
                return Results.BadRequest( new { message = $"Invalid page key: {pageKey}" } );
            }

            SavedFilter? entity = await dbContext.SavedFilters
                .FirstOrDefaultAsync( f => f.Id == id && f.PageKey == pageKey, ct );

            if (entity is null) {
                return Results.NotFound( );
            }

            entity.IsShared = !entity.IsShared;
            entity.LastUpdated = DateTime.UtcNow;
            entity.Version++;
            _ = await dbContext.SaveChangesAsync( ct );

            return Results.Ok( new { entity.Id, entity.IsShared } );
        } )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }

    private static bool IsValidJson( string json ) {
        try {
            using JsonDocument doc = JsonDocument.Parse( json );
            return true;
        } catch (JsonException) {
            return false;
        }
    }
}

/// <summary>Request body for creating a saved filter.</summary>
internal sealed record CreateFilterRequest( string Name, string CriteriaJson );

/// <summary>Request body for updating a saved filter.</summary>
internal sealed record UpdateFilterRequest( string? Name, string? CriteriaJson );
