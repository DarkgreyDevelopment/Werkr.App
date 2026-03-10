using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Models;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Extension methods for mapping workflow REST endpoints to the application.
/// </summary>
internal static class WorkflowEndpoints {

    /// <summary>Maps all workflow-related REST endpoints.</summary>
    public static WebApplication MapWorkflowEndpoints( this WebApplication app ) {
        MapWorkflowCrud( app );
        MapWorkflowSteps( app );
        MapStepDependencies( app );
        MapWorkflowExecution( app );
        return app;
    }

    // ── Workflow CRUD ──

    /// <summary>
    /// Registers CRUD and enable/disable endpoints for workflows.
    /// </summary>
    private static void MapWorkflowCrud( WebApplication app ) {

        _ = app.MapGet( "/api/workflows", async (
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            IReadOnlyList<Workflow> workflows = await workflowService.GetAllAsync( ct );
            List<WorkflowDto> dtos = [.. workflows.Select( WorkflowMapper.ToDto )];
            return Results.Ok( dtos );
        } )
        .WithName( "GetWorkflows" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/workflows/{id}", async (
            long id,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            Workflow? workflow = await workflowService.GetByIdAsync( id, ct );
            return workflow is null
                ? Results.NotFound( )
                : Results.Ok( WorkflowMapper.ToDto( workflow ) );
        } )
        .WithName( "GetWorkflow" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/workflows", async (
            WorkflowCreateRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                Workflow entity = WorkflowMapper.ToEntity( request );
                Workflow created = await workflowService.CreateAsync( entity, ct );
                WorkflowDto dto = WorkflowMapper.ToDto( created );
                return Results.Created( $"/api/workflows/{dto.Id}", dto );
            } catch (ValidationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "CreateWorkflow" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapPut( "/api/workflows/{id}", async (
            long id,
            WorkflowUpdateRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                Workflow entity = WorkflowMapper.ToEntity( id, request );
                Workflow updated = await workflowService.UpdateAsync( entity, ct );
                return Results.Ok( WorkflowMapper.ToDto( updated ) );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (ValidationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "UpdateWorkflow" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapDelete( "/api/workflows/{id}", async (
            long id,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                await workflowService.DeleteAsync( id, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "DeleteWorkflow" )
        .RequireAuthorization( Policies.CanDelete );

        _ = app.MapPatch( "/api/workflows/{id}/enabled", async (
            long id,
            WorkflowSetEnabledRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            Workflow? workflow = await workflowService.GetByIdAsync( id, ct );
            if (workflow is null) {
                return Results.NotFound( );
            }

            workflow.Enabled = request.Enabled;
            _ = await workflowService.UpdateAsync( workflow, ct );
            return Results.Ok( WorkflowMapper.ToDto( workflow ) );
        } )
        .WithName( "SetWorkflowEnabled" )
        .RequireAuthorization( Policies.CanUpdate );
    }

    // ── Workflow Steps ──

    /// <summary>
    /// Registers endpoints for managing steps within a workflow.
    /// </summary>
    private static void MapWorkflowSteps( WebApplication app ) {

        _ = app.MapPost( "/api/workflows/{workflowId}/steps", async (
            long workflowId,
            WorkflowStepCreateRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                WorkflowStep step = WorkflowMapper.ToStepEntity( workflowId, request );
                WorkflowStep created = await workflowService.AddStepAsync( workflowId, step, ct );
                WorkflowStepDto dto = WorkflowMapper.ToStepDto( created );
                return Results.Created( $"/api/workflows/{workflowId}/steps/{dto.Id}", dto );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "AddWorkflowStep" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapPut( "/api/workflows/{workflowId}/steps/{stepId}", async (
            long workflowId,
            long stepId,
            WorkflowStepUpdateRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                WorkflowStep step = new( ) {
                    Id = stepId,
                    WorkflowId = workflowId,
                    Order = request.Order,
                    ControlStatement = Enum.Parse<ControlStatement>(
                            request.ControlStatement, ignoreCase: true ),
                    ConditionExpression = request.ConditionExpression,
                    MaxIterations = request.MaxIterations,
                    AgentConnectionIdOverride = request.AgentConnectionIdOverride,
                    DependencyMode = Enum.Parse<DependencyMode>(
                            request.DependencyMode, ignoreCase: true ),
                    InputVariableName = request.InputVariableName,
                    OutputVariableName = request.OutputVariableName,
                };
                WorkflowStep updated = await workflowService.UpdateStepAsync( step, ct );
                return Results.Ok( WorkflowMapper.ToStepDto( updated ) );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "UpdateWorkflowStep" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapDelete( "/api/workflows/{workflowId}/steps/{stepId}", async (
            long workflowId,
            long stepId,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                await workflowService.RemoveStepAsync( stepId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "RemoveWorkflowStep" )
        .RequireAuthorization( Policies.CanDelete );
    }

    // ── Step Dependencies ──

    /// <summary>
    /// Registers endpoints for managing step-to-step dependencies within a workflow.
    /// </summary>
    private static void MapStepDependencies( WebApplication app ) {

        _ = app.MapPost( "/api/workflows/{workflowId}/steps/{stepId}/dependencies", async (
            long workflowId,
            long stepId,
            StepDependencyRequest request,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                await workflowService.AddStepDependencyAsync(
                    stepId, request.DependsOnStepId, ct );
                StepDependencyDto depDto = new( StepId: stepId, DependsOnStepId: request.DependsOnStepId );
                return Results.Created(
                    $"/api/workflows/{workflowId}/steps/{stepId}/dependencies/{request.DependsOnStepId}",
                    depDto );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (InvalidOperationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "AddStepDependency" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapDelete( "/api/workflows/{workflowId}/steps/{stepId}/dependencies/{dependsOnStepId}", async (
            long workflowId,
            long stepId,
            long dependsOnStepId,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                await workflowService.RemoveStepDependencyAsync( stepId, dependsOnStepId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "RemoveStepDependency" )
        .RequireAuthorization( Policies.CanDelete );
    }

    // ── Workflow Execution & Runs ──

    /// <summary>
    /// Registers endpoints for workflow validation, execution, run history, and live step-status streaming.
    /// </summary>
    private static void MapWorkflowExecution( WebApplication app ) {

        _ = app.MapPost( "/api/workflows/{id}/validate", async (
            long id,
            WorkflowService workflowService,
            CancellationToken ct
        ) => {
            try {
                _ = await workflowService.ValidateDagAsync( id, ct );
                return Results.Ok( new DagValidationResult(
                    IsValid: true, Errors: [] ) );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (InvalidOperationException ex) {
                return Results.Ok( new DagValidationResult(
                    IsValid: false, Errors: [ex.Message] ) );
            }
        } )
        .WithName( "ValidateWorkflow" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/workflows/{id}/execute", async (
            long id,
            WorkflowRunRequest? request,
            WorkflowService workflowService,
            RunNowService runNowService,
            ScheduleInvalidationDispatcher invalidationDispatcher,
            CancellationToken ct
        ) => {
            Workflow? workflow = await workflowService.GetByIdAsync( id, ct );
            if (workflow is null) {
                return Results.NotFound( );
            }

            if (!workflow.Enabled) {
                return Results.BadRequest( new { message = "Workflow is disabled." } );
            }

            Dictionary<string, string>? triggerVariables = request?.Variables;
            (Guid scheduleId, Guid workflowRunId) = await runNowService.CreateWorkflowRunNowAsync(
                id, triggerVariables: triggerVariables, ct: ct );
            await invalidationDispatcher.InvalidateAsync( scheduleId, ct );
            return Results.Accepted( $"/api/workflows/{id}/runs",
                new { scheduleId, workflowRunId, message = "One-time schedule created. Execution will begin on the next agent sync." } );
        } )
        .WithName( "ExecuteWorkflow" )
        .RequireAuthorization( Policies.CanExecute );

        _ = app.MapGet( "/api/workflows/{id}/runs", async (
            long id,
            int? limit,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            IReadOnlyList<WorkflowRun> runs = await dbContext.WorkflowRuns.AsNoTracking( )
                .Where( r => r.WorkflowId == id )
                .OrderByDescending( r => r.StartTime )
                .Take( limit ?? 50 )
                .ToListAsync( ct );
            List<WorkflowRunDto> dtos = [.. runs.Select( WorkflowMapper.ToRunDto )];
            return Results.Ok( dtos );
        } )
        .WithName( "GetWorkflowRuns" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/workflows/runs/{runId}", async (
            Guid runId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            WorkflowRun? run = await dbContext.WorkflowRuns.AsNoTracking( )
                .Include( r => r.Jobs )
                .FirstOrDefaultAsync( r => r.Id == runId, ct );
            return run is null
                ? Results.NotFound( )
                : Results.Ok( WorkflowMapper.ToRunDetailDto( run ) );
        } )
        .WithName( "GetWorkflowRun" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/workflows/runs/{runId}/stream", async (
            Guid runId,
            JobEventBroadcaster broadcaster,
            WerkrDbContext dbContext,
            HttpContext httpContext,
            CancellationToken ct
        ) => {
            // Verify the run exists before opening the SSE stream.
            bool exists = await dbContext.WorkflowRuns.AsNoTracking( )
                .AnyAsync( r => r.Id == runId, ct );
            if (!exists) {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";

            JsonSerializerOptions jsonOptions = new( ) {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            using JobEventSubscription subscription = broadcaster.Subscribe( );

            try {
                await foreach (JobEvent jobEvent in subscription.Reader.ReadAllAsync( ct )) {
                    // Only forward events belonging to this workflow run.
                    if (jobEvent.WorkflowRunId != runId) {
                        continue;
                    }

                    string json = JsonSerializer.Serialize( jobEvent, jsonOptions );
                    await httpContext.Response.WriteAsync( $"event: workflow-job\n", ct );
                    await httpContext.Response.WriteAsync( $"data: {json}\n\n", ct );
                    await httpContext.Response.Body.FlushAsync( ct );
                }
            } catch (OperationCanceledException) {
                // Client disconnected — normal SSE lifecycle.
            }
        } )
        .WithName( "StreamWorkflowRunUpdates" )
        .RequireAuthorization( Policies.CanRead )
        .ExcludeFromDescription( );
    }
}
