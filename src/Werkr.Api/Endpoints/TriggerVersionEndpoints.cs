using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Triggers;
using Werkr.Data.Entities.Triggers;

namespace Werkr.Api.Endpoints;

/// <summary>Maps trigger version history endpoints.</summary>
internal static class TriggerVersionEndpoints {

    /// <summary>Maps version history and detail endpoints under <c>/api/v1/triggers/file-monitor/{triggerId}/versions</c>.</summary>
    public static WebApplication MapTriggerVersionEndpoints( this WebApplication app ) {

        // GET /api/v1/triggers/file-monitor/{triggerId}/versions — paginated version list
        _ = app.MapGet( "/api/v1/triggers/file-monitor/{triggerId}/versions", async (
            long triggerId,
            int? limit,
            int? offset,
            TriggerVersionService versionService,
            CancellationToken ct
        ) => {
            int effectiveLimit = Math.Clamp( limit ?? 25, 1, 100 );
            int effectiveOffset = Math.Max( offset ?? 0, 0 );

            PagedResult<TriggerVersion> result = await versionService.GetVersionsAsync(
                triggerId, effectiveLimit, effectiveOffset, ct );

            PagedResult<TriggerVersionDto> dtoResult = new(
                [.. result.Items.Select( ToDto )],
                result.TotalCount,
                result.Limit,
                result.Offset );

            return Results.Ok( dtoResult );
        } )
        .WithName( "GetTriggerVersions" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/triggers/file-monitor/{triggerId}/versions/{versionId:long} — single version detail
        _ = app.MapGet( "/api/v1/triggers/file-monitor/{triggerId}/versions/{versionId:long}", async (
            long triggerId,
            long versionId,
            TriggerVersionService versionService,
            CancellationToken ct
        ) => {
            TriggerVersion? version = await versionService.GetVersionByIdAsync( triggerId, versionId, ct );
            return version is null
                ? Results.NotFound( )
                : Results.Ok( ToDto( version ) );
        } )
        .WithName( "GetTriggerVersion" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }

    private static TriggerVersionDto ToDto( TriggerVersion version ) =>
        new(
            Id: version.Id,
            TriggerId: version.TriggerId,
            VersionNumber: version.VersionNumber,
            Definition: version.Definition,
            CreatedUtc: version.Created,
            CreatedByUserId: version.CreatedByUserId,
            ChangeDescription: version.ChangeDescription );
}

/// <summary>Response DTO for a trigger version snapshot.</summary>
internal sealed record TriggerVersionDto(
    long Id,
    long TriggerId,
    int VersionNumber,
    string Definition,
    DateTime CreatedUtc,
    string? CreatedByUserId,
    string? ChangeDescription
);
