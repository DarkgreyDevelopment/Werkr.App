using Werkr.Api.Models;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Tasks;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Endpoints;

/// <summary>Maps task version history and diff endpoints.</summary>
internal static class TaskVersionEndpoints {
    /// <summary>Maps version history, detail, and diff endpoints under <c>/api/v1/tasks/{taskId}/versions</c>.</summary>
    public static WebApplication MapTaskVersionEndpoints( this WebApplication app ) {

        // GET /api/v1/tasks/{taskId}/versions — paginated version list
        _ = app.MapGet( "/api/v1/tasks/{taskId}/versions", async (
            long taskId,
            int? limit,
            int? offset,
            TaskVersionService versionService,
            CancellationToken ct
        ) => {
            int effectiveLimit = Math.Clamp( limit ?? 25, 1, 100 );
            int effectiveOffset = Math.Max( offset ?? 0, 0 );

            PagedResult<TaskVersion> result = await versionService.GetVersionsAsync(
                taskId, effectiveLimit, effectiveOffset, ct );

            PagedResult<TaskVersionDto> dtoResult = new(
                [.. result.Items.Select( TaskMapper.ToVersionDto )],
                result.TotalCount,
                result.Limit,
                result.Offset );

            return Results.Ok( dtoResult );
        } )
        .WithName( "GetTaskVersions" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/tasks/{taskId}/versions/{versionId:long} — single version detail
        _ = app.MapGet( "/api/v1/tasks/{taskId}/versions/{versionId:long}", async (
            long taskId,
            long versionId,
            TaskVersionService versionService,
            CancellationToken ct
        ) => {
            TaskVersion? version = await versionService.GetVersionByIdAsync( taskId, versionId, ct );
            return version is null
                ? Results.NotFound( )
                : Results.Ok( TaskMapper.ToVersionDto( version ) );
        } )
        .WithName( "GetTaskVersion" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/tasks/{taskId}/versions/diff?from=&to= — on-demand diff
        _ = app.MapGet( "/api/v1/tasks/{taskId}/versions/diff", async (
            long taskId,
            long from,
            long to,
            TaskVersionService versionService,
            TaskVersionDiffService diffService,
            CancellationToken ct
        ) => {
            // Validate both versions belong to the task
            TaskVersion? fromVersion = await versionService.GetVersionByIdAsync( taskId, from, ct );
            TaskVersion? toVersion = await versionService.GetVersionByIdAsync( taskId, to, ct );

            if (fromVersion is null || toVersion is null) {
                return Results.NotFound( new { message = "One or both version IDs not found for this task." } );
            }

            IReadOnlyList<TaskVersionDiffEntry>? changes = await diffService.ComputeDiffAsync( from, to, ct );
            if (changes is null) {
                return Results.NotFound( new { message = "Could not compute diff." } );
            }

            TaskVersionDiffResponse response = new(
                FromVersionId: fromVersion.Id,
                FromVersionNumber: fromVersion.VersionNumber,
                ToVersionId: toVersion.Id,
                ToVersionNumber: toVersion.VersionNumber,
                Changes: changes );

            return Results.Ok( response );
        } )
        .WithName( "GetTaskVersionDiff" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
