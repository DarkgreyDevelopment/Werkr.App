using System.Security.Claims;
using Werkr.Api.Models;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Workflows;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Endpoints;

/// <summary>Maps workflow version history, detail, diff, and rollback endpoints.</summary>
internal static class WorkflowVersionEndpoints {
    /// <summary>Maps version history, detail, diff, and rollback endpoints under <c>/api/v1/workflows/{workflowId}/versions</c>.</summary>
    public static WebApplication MapWorkflowVersionEndpoints( this WebApplication app ) {

        // GET /api/v1/workflows/{workflowId}/versions — paginated version list
        _ = app.MapGet( "/api/v1/workflows/{workflowId}/versions", async (
            long workflowId,
            int? limit,
            int? offset,
            WorkflowVersionService versionService,
            CancellationToken ct
        ) => {
            int effectiveLimit = Math.Clamp( limit ?? 25, 1, 100 );
            int effectiveOffset = Math.Max( offset ?? 0, 0 );

            PagedResult<WorkflowVersion> result = await versionService.GetVersionsAsync(
                workflowId, effectiveLimit, effectiveOffset, ct );

            PagedResult<WorkflowVersionDto> dtoResult = new(
                [.. result.Items.Select( WorkflowMapper.ToVersionDto )],
                result.TotalCount,
                result.Limit,
                result.Offset );

            return Results.Ok( dtoResult );
        } )
        .WithName( "GetWorkflowVersions" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/workflows/{workflowId}/versions/{versionId:long} — single version detail
        _ = app.MapGet( "/api/v1/workflows/{workflowId}/versions/{versionId:long}", async (
            long workflowId,
            long versionId,
            WorkflowVersionService versionService,
            CancellationToken ct
        ) => {
            WorkflowVersion? version = await versionService.GetVersionByIdAsync( workflowId, versionId, ct );
            return version is null
                ? Results.NotFound( )
                : Results.Ok( WorkflowMapper.ToVersionDto( version ) );
        } )
        .WithName( "GetWorkflowVersion" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/workflows/{workflowId}/versions/diff?from=&to= — on-demand diff
        _ = app.MapGet( "/api/v1/workflows/{workflowId}/versions/diff", async (
            long workflowId,
            long from,
            long to,
            WorkflowVersionService versionService,
            WorkflowVersionDiffService diffService,
            CancellationToken ct
        ) => {
            // Validate both versions belong to the workflow
            WorkflowVersion? fromVersion = await versionService.GetVersionByIdAsync( workflowId, from, ct );
            WorkflowVersion? toVersion = await versionService.GetVersionByIdAsync( workflowId, to, ct );

            if (fromVersion is null || toVersion is null) {
                return Results.NotFound( new { message = "One or both version IDs not found for this workflow." } );
            }

            IReadOnlyList<WorkflowVersionDiffEntry>? changes = await diffService.ComputeDiffAsync( from, to, ct );
            if (changes is null) {
                return Results.NotFound( new { message = "Could not compute diff." } );
            }

            WorkflowVersionDiffResponse response = new(
                FromVersionId: fromVersion.Id,
                FromVersionNumber: fromVersion.VersionNumber,
                ToVersionId: toVersion.Id,
                ToVersionNumber: toVersion.VersionNumber,
                Changes: changes );

            return Results.Ok( response );
        } )
        .WithName( "GetWorkflowVersionDiff" )
        .RequireAuthorization( Policies.CanRead );

        // POST /api/v1/workflows/{workflowId}/versions/{versionId:long}/rollback — rollback to a version
        _ = app.MapPost( "/api/v1/workflows/{workflowId}/versions/{versionId:long}/rollback", async (
            long workflowId,
            long versionId,
            HttpContext httpContext,
            WorkflowVersionService versionService,
            CancellationToken ct
        ) => {
            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            WorkflowVersion? newVersion = await versionService.RollbackAsync( workflowId, versionId, userId, ct );

            return newVersion is null
                ? Results.NotFound( new { message = "Version or workflow not found." } )
                : Results.Ok( WorkflowMapper.ToVersionDto( newVersion ) );
        } )
        .WithName( "RollbackWorkflowVersion" )
        .RequireAuthorization( Policies.CanUpdate );

        return app;
    }
}
