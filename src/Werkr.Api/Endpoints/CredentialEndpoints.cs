using System.Security.Claims;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Credentials;

namespace Werkr.Api.Endpoints;

/// <summary>Maps credential management REST endpoints.</summary>
internal static class CredentialEndpoints {
    /// <summary>Maps credential CRUD, rename, scope, and delete endpoints.</summary>
    public static WebApplication MapCredentialEndpoints( this WebApplication app ) {

        _ = app.MapGet( "/api/v1/settings/credentials", async (
            ICredentialService credService,
            CancellationToken ct
        ) => {
            IReadOnlyList<CredentialDto> credentials = await credService.GetAllAsync( ct );
            return Results.Ok( credentials );
        } )
        .WithName( "GetCredentials" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPost( "/api/v1/settings/credentials", async (
            CredentialCreateRequest request,
            HttpContext httpContext,
            ICredentialService credService,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                CredentialCreateResponse response = await credService.CreateAsync( request, userId, ct );
                return Results.Created( $"/api/v1/settings/credentials/{response.Id}", response );
            } catch (ArgumentException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "CreateCredential" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/v1/settings/credentials/{id}", async (
            long id,
            CredentialUpdateRequest request,
            HttpContext httpContext,
            ICredentialService credService,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                CredentialDto updated = await credService.UpdateAsync( id, request, userId, ct );
                return Results.Ok( updated );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "UpdateCredential" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/v1/settings/credentials/{id}/name", async (
            long id,
            CredentialRenameRequest request,
            HttpContext httpContext,
            ICredentialService credService,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                CredentialDto updated = await credService.RenameAsync( id, request.NewName, userId, ct );
                return Results.Ok( updated );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "RenameCredential" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapDelete( "/api/v1/settings/credentials/{id}", async (
            long id,
            HttpContext httpContext,
            ICredentialService credService,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                await credService.DeleteAsync( id, userId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (InvalidOperationException ex) {
                return Results.Conflict( new { message = ex.Message } );
            }
        } )
        .WithName( "DeleteCredential" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/v1/settings/credentials/{id}/scope", async (
            long id,
            CredentialScopeUpdateRequest request,
            HttpContext httpContext,
            ICredentialService credService,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                await credService.UpdateScopesAsync( id, request.AgentConnectionIds, userId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "UpdateCredentialScope" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
