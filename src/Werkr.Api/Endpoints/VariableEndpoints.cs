using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Api.Models;
using Werkr.Common.Auth;
using Werkr.Common.Configuration;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Extension methods for mapping workflow-variable REST endpoints to the application.
/// Covers design-time variable definitions and runtime (per-run) variable queries/edits.
/// </summary>
internal static class VariableEndpoints {

    /// <summary>Maps all variable-related REST endpoints.</summary>
    public static WebApplication MapVariableEndpoints( this WebApplication app ) {
        MapDefinitionEndpoints( app );
        MapRuntimeEndpoints( app );
        return app;
    }

    // ── Design-time Variable Definitions ──

    /// <summary>
    /// CRUD endpoints for workflow variable definitions (design-time).
    /// </summary>
    private static void MapDefinitionEndpoints( WebApplication app ) {

        _ = app.MapGet( "/api/workflows/{workflowId}/variables", async (
            long workflowId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            bool workflowExists = await dbContext.Workflows.AsNoTracking( )
                .AnyAsync( w => w.Id == workflowId, ct );
            if (!workflowExists) {
                return Results.NotFound( );
            }

            List<WorkflowVariableDto> dtos = await dbContext.WorkflowVariables.AsNoTracking( )
                .Where( v => v.WorkflowId == workflowId )
                .OrderBy( v => v.Name )
                .Select( v => WorkflowMapper.ToVariableDto( v ) )
                .ToListAsync( ct );

            return Results.Ok( dtos );
        } )
        .WithName( "GetWorkflowVariables" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPost( "/api/workflows/{workflowId}/variables", async (
            long workflowId,
            CreateVariableRequest request,
            WerkrDbContext dbContext,
            IOptions<WorkflowVariableOptions> variableOptions,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.Name )) {
                return Results.BadRequest( new { message = "Variable name is required." } );
            }

            if (request.Name.Length > 128) {
                return Results.BadRequest( new { message = "Variable name must not exceed 128 characters." } );
            }

            if (request.DefaultValue is not null) {
                string? validationError = ValidateJsonValue( request.DefaultValue, variableOptions.Value );
                if (validationError is not null) {
                    return Results.BadRequest( new { message = validationError } );
                }
            }

            bool workflowExists = await dbContext.Workflows.AsNoTracking( )
                .AnyAsync( w => w.Id == workflowId, ct );
            if (!workflowExists) {
                return Results.NotFound( );
            }

            bool nameExists = await dbContext.WorkflowVariables.AsNoTracking( )
                .AnyAsync( v => v.WorkflowId == workflowId
                    && v.Name.Equals( request.Name, StringComparison.CurrentCultureIgnoreCase ), ct );
            if (nameExists) {
                return Results.BadRequest( new { message = $"A variable named '{request.Name}' already exists on this workflow." } );
            }

            WorkflowVariable entity = new( ) {
                WorkflowId = workflowId,
                Name = request.Name.ToLowerInvariant( ),
                Description = request.Description,
                DefaultValue = request.DefaultValue,
                DataType = request.DataType,
                IsRequired = request.IsRequired,
                LogRedaction = request.LogRedaction,
            };

            _ = dbContext.WorkflowVariables.Add( entity );
            _ = await dbContext.SaveChangesAsync( ct );

            WorkflowVariableDto dto = WorkflowMapper.ToVariableDto( entity );
            return Results.Created( $"/api/workflows/{workflowId}/variables/{dto.Id}", dto );
        } )
        .WithName( "CreateWorkflowVariable" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/workflows/{workflowId}/variables/{variableId}", async (
            long workflowId,
            long variableId,
            UpdateVariableRequest request,
            WerkrDbContext dbContext,
            IOptions<WorkflowVariableOptions> variableOptions,
            CancellationToken ct
        ) => {
            WorkflowVariable? entity = await dbContext.WorkflowVariables
                .FirstOrDefaultAsync( v => v.Id == variableId && v.WorkflowId == workflowId, ct );
            if (entity is null) {
                return Results.NotFound( );
            }

            if (request.Name is not null) {
                if (string.IsNullOrWhiteSpace( request.Name )) {
                    return Results.BadRequest( new { message = "Variable name must not be empty." } );
                }

                if (request.Name.Length > 128) {
                    return Results.BadRequest( new { message = "Variable name must not exceed 128 characters." } );
                }

                // Check uniqueness if name is changing
                if (!string.Equals( entity.Name, request.Name, StringComparison.OrdinalIgnoreCase )) {
                    bool nameExists = await dbContext.WorkflowVariables.AsNoTracking( )
                        .AnyAsync( v => v.WorkflowId == workflowId
                                     && v.Name.Equals( request.Name, StringComparison.CurrentCultureIgnoreCase )
                                     && v.Id != variableId, ct );
                    if (nameExists) {
                        return Results.BadRequest( new { message = $"A variable named '{request.Name}' already exists on this workflow." } );
                    }
                }

                entity.Name = request.Name.ToLowerInvariant( );
            }

            if (request.Description is not null) {
                entity.Description = request.Description;
            }

            if (request.DefaultValue is not null) {
                string? validationError = ValidateJsonValue( request.DefaultValue, variableOptions.Value );
                if (validationError is not null) {
                    return Results.BadRequest( new { message = validationError } );
                }

                entity.DefaultValue = request.DefaultValue;
            }

            if (request.DataType is not null) {
                entity.DataType = request.DataType;
            }

            if (request.IsRequired is not null) {
                entity.IsRequired = request.IsRequired.Value;
            }

            if (request.LogRedaction is not null) {
                entity.LogRedaction = request.LogRedaction.Value;
            }

            _ = await dbContext.SaveChangesAsync( ct );
            return Results.Ok( WorkflowMapper.ToVariableDto( entity ) );
        } )
        .WithName( "UpdateWorkflowVariable" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapDelete( "/api/workflows/{workflowId}/variables/{variableId}", async (
            long workflowId,
            long variableId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            WorkflowVariable? entity = await dbContext.WorkflowVariables
                .FirstOrDefaultAsync( v => v.Id == variableId && v.WorkflowId == workflowId, ct );
            if (entity is null) {
                return Results.NotFound( );
            }

            _ = dbContext.WorkflowVariables.Remove( entity );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.NoContent( );
        } )
        .WithName( "DeleteWorkflowVariable" )
        .RequireAuthorization( Policies.IsAdmin );
    }

    // ── Runtime Variable Endpoints ──

    /// <summary>
    /// Query and edit endpoints for runtime (per-run) variable values.
    /// </summary>
    private static void MapRuntimeEndpoints( WebApplication app ) {

        _ = app.MapGet( "/api/workflow-runs/{runId}/variables", async (
            Guid runId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            bool runExists = await dbContext.WorkflowRuns.AsNoTracking( )
                .AnyAsync( r => r.Id == runId, ct );
            if (!runExists) {
                return Results.NotFound( );
            }

            // Return only the latest version of each variable
            List<RunVariableCurrentDto> dtos = await dbContext.WorkflowRunVariables.AsNoTracking( )
                .Where( v => v.WorkflowRunId == runId )
                .GroupBy( v => v.VariableName )
                .Select( g => g.OrderByDescending( v => v.Version ).First( ) )
                .Select( v => WorkflowMapper.ToRunVariableCurrentDto( v ) )
                .ToListAsync( ct );

            return Results.Ok( dtos );
        } )
        .WithName( "GetRunVariables" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/workflow-runs/{runId}/variables/{name}/history", async (
            Guid runId,
            string name,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            bool runExists = await dbContext.WorkflowRuns.AsNoTracking( )
                .AnyAsync( r => r.Id == runId, ct );
            if (!runExists) {
                return Results.NotFound( );
            }

            List<RunVariableVersionDto> dtos = await dbContext.WorkflowRunVariables.AsNoTracking( )
                .Where( v => v.WorkflowRunId == runId && v.VariableName == name )
                .OrderBy( v => v.Version )
                .Select( v => WorkflowMapper.ToRunVariableVersionDto( v ) )
                .ToListAsync( ct );

            return Results.Ok( dtos );
        } )
        .WithName( "GetRunVariableHistory" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/workflow-runs/{runId}/variables/{name}", async (
            Guid runId,
            string name,
            EditVariableRequest request,
            WerkrDbContext dbContext,
            IOptions<WorkflowVariableOptions> variableOptions,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.Value )) {
                return Results.BadRequest( new { message = "Value is required." } );
            }

            string? validationError = ValidateJsonValue( request.Value, variableOptions.Value );
            if (validationError is not null) {
                return Results.BadRequest( new { message = validationError } );
            }

            WorkflowRun? run = await dbContext.WorkflowRuns.AsNoTracking( )
                .FirstOrDefaultAsync( r => r.Id == runId, ct );
            if (run is null) {
                return Results.NotFound( );
            }

            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
                await dbContext.Database.BeginTransactionAsync( ct );

            // Determine next version number
            int maxVersion = await dbContext.WorkflowRunVariables.AsNoTracking( )
                .Where( v => v.WorkflowRunId == runId && v.VariableName == name )
                .Select( v => v.Version )
                .DefaultIfEmpty( 0 )
                .MaxAsync( ct );

            WorkflowRunVariable entity = new( ) {
                WorkflowRunId = runId,
                VariableName = name,
                Value = request.Value,
                Version = maxVersion + 1,
                Source = VariableSource.ReExecutionEdit,
                Created = DateTime.UtcNow,
            };

            _ = dbContext.WorkflowRunVariables.Add( entity );
            _ = await dbContext.SaveChangesAsync( ct );
            await tx.CommitAsync( ct );

            return Results.Ok( WorkflowMapper.ToRunVariableVersionDto( entity ) );
        } )
        .WithName( "EditRunVariable" )
        .RequireAuthorization( Policies.IsAdmin );
    }

    // ── Helpers ──

    /// <summary>
    /// Validates that a value is valid JSON and does not exceed the configured size limit.
    /// Returns <see langword="null"/> if valid, or an error message string if invalid.
    /// </summary>
    private static string? ValidateJsonValue( string value, WorkflowVariableOptions options ) {
        int sizeBytes = Encoding.UTF8.GetByteCount( value );
        if (sizeBytes > options.MaxValueSizeBytes) {
            return $"Variable value exceeds the maximum allowed size of {options.MaxValueSizeBytes} bytes (actual: {sizeBytes} bytes).";
        }

        try {
            using JsonDocument doc = JsonDocument.Parse( value );
        } catch (JsonException ex) {
            return $"Variable value is not valid JSON: {ex.Message}";
        }

        return null;
    }
}
