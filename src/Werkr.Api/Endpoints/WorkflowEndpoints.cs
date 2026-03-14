using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Models;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Extension methods for mapping workflow REST endpoints to the application.
/// </summary>
internal static partial class WorkflowEndpoints {

    /// <summary>Maps all workflow-related REST endpoints.</summary>
    public static WebApplication MapWorkflowEndpoints( this WebApplication app ) {
        MapWorkflowCrud( app );
        MapWorkflowSteps( app );
        MapStepDependencies( app );
        MapWorkflowSchedules( app );
        MapWorkflowExecution( app );
        MapWorkflowDashboard( app );
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
                // Validate annotations
                if (request.Annotations is { Count: > 50 }) {
                    return Results.BadRequest( new { message = "Maximum 50 annotations allowed." } );
                }

                if (request.Annotations is not null) {
                    string serialized = JsonSerializer.Serialize( request.Annotations );
                    if (serialized.Length > 65536) {
                        return Results.BadRequest( new { message = "Annotations JSON exceeds 64 KB limit." } );
                    }

                    // Strip HTML tags from annotation text for XSS safety
                    List<AnnotationDto> sanitized = [];
                    foreach (AnnotationDto ann in request.Annotations) {
                        string cleanText = StripHtmlTags( ann.Text );
                        sanitized.Add( ann with { Text = cleanText } );
                    }
                    // Validate annotation fields explicitly (DataAnnotations are not auto-enforced in Minimal APIs)
                    foreach (AnnotationDto ann in sanitized) {
                        if (ann.Text.Length > 500) {
                            return Results.BadRequest( new { message = $"Annotation text exceeds 500 characters (id: {ann.Id})." } );
                        }

                        if (ann.X < -10000 || ann.X > 50000 || ann.Y < -10000 || ann.Y > 50000) {
                            return Results.BadRequest( new { message = $"Annotation position out of bounds (id: {ann.Id})." } );
                        }

                        if (ann.Width < 80 || ann.Width > 800 || ann.Height < 40 || ann.Height > 600) {
                            return Results.BadRequest( new { message = $"Annotation dimensions out of bounds (id: {ann.Id})." } );
                        }

                        if (!HexColorRegex( ).IsMatch( ann.Color )) {
                            return Results.BadRequest( new { message = $"Annotation color must be a hex color (id: {ann.Id})." } );
                        }
                    }

                    request = request with { Annotations = sanitized };
                }

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

        _ = app.MapPost( "/api/workflows/{workflowId}/steps/batch", async (
            long workflowId,
            WorkflowStepBatchRequest request,
            WorkflowService workflowService,
            Microsoft.AspNetCore.Http.HttpContext httpContext,
            CancellationToken ct
        ) => {
            // Dynamic per-operation-type authorization
            Microsoft.AspNetCore.Authorization.IAuthorizationService authService =
                httpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

            bool needsCreate = request.Operations.Any( o =>
                string.Equals( o.OperationType, "Add", StringComparison.OrdinalIgnoreCase ) );
            bool needsUpdate = request.Operations.Any( o =>
                string.Equals( o.OperationType, "Update", StringComparison.OrdinalIgnoreCase ) );
            bool needsDelete = request.Operations.Any( o =>
                string.Equals( o.OperationType, "Delete", StringComparison.OrdinalIgnoreCase ) );

            // Check dependency changes too
            foreach (StepBatchOperation op in request.Operations) {
                if (op.DependencyChanges is null) {
                    continue;
                }

                foreach (DependencyBatchItem dep in op.DependencyChanges) {
                    if (string.Equals( dep.OperationType, "Add", StringComparison.OrdinalIgnoreCase )) {
                        needsCreate = true;
                    } else if (string.Equals( dep.OperationType, "Delete", StringComparison.OrdinalIgnoreCase )) {
                        needsDelete = true;
                    }
                }
            }

            List<string> missingPolicies = [];
            if (needsCreate) {
                Microsoft.AspNetCore.Authorization.AuthorizationResult result =
                    await authService.AuthorizeAsync( httpContext.User, null, Policies.CanCreate );
                if (!result.Succeeded) {
                    missingPolicies.Add( Policies.CanCreate );
                }
            }
            if (needsUpdate) {
                Microsoft.AspNetCore.Authorization.AuthorizationResult result =
                    await authService.AuthorizeAsync( httpContext.User, null, Policies.CanUpdate );
                if (!result.Succeeded) {
                    missingPolicies.Add( Policies.CanUpdate );
                }
            }
            if (needsDelete) {
                Microsoft.AspNetCore.Authorization.AuthorizationResult result =
                    await authService.AuthorizeAsync( httpContext.User, null, Policies.CanDelete );
                if (!result.Succeeded) {
                    missingPolicies.Add( Policies.CanDelete );
                }
            }

            if (missingPolicies.Count > 0) {
                return Results.Forbid( );
            }

            try {
                WorkflowStepBatchResponse response =
                    await workflowService.BatchUpdateStepsAsync( workflowId, request, ct );
                return response.Success
                    ? Results.Ok( response )
                    : Results.BadRequest( response );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "BatchUpdateWorkflowSteps" )
        .RequireAuthorization( Policies.CanRead );
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

    // ── Workflow Schedules ──

    /// <summary>
    /// Registers endpoints for associating schedules with a workflow.
    /// </summary>
    private static void MapWorkflowSchedules( WebApplication app ) {

        _ = app.MapGet( "/api/workflows/{workflowId}/schedules", async (
            long workflowId,
            WerkrDbContext dbContext,
            ScheduleService scheduleService,
            CancellationToken ct
        ) => {
            List<WorkflowSchedule> links = await dbContext.WorkflowSchedules.AsNoTracking( )
                .Where( ws => ws.WorkflowId == workflowId )
                .ToListAsync( ct );

            List<ScheduleDto> dtos = [];
            foreach (WorkflowSchedule ws in links) {
                Schedule? schedule = await scheduleService.GetByIdAsync( ws.ScheduleId, ct );
                if (schedule is not null) {
                    dtos.Add( ScheduleMapper.ToDto( schedule ) );
                }
            }
            return Results.Ok( dtos );
        } )
        .WithName( "GetWorkflowSchedules" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/workflows/{workflowId}/schedules", async (
            long workflowId,
            WorkflowScheduleAssociateRequest request,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            bool workflowExists = await dbContext.Workflows.AnyAsync(
                w => w.Id == workflowId, ct );
            if (!workflowExists) {
                return Results.NotFound( new { message = "Workflow not found." } );
            }

            bool scheduleExists = await dbContext.Schedules.AnyAsync(
                s => s.Id == request.ScheduleId, ct );
            if (!scheduleExists) {
                return Results.NotFound( new { message = "Schedule not found." } );
            }

            bool alreadyLinked = await dbContext.WorkflowSchedules.AnyAsync(
                ws => ws.WorkflowId == workflowId && ws.ScheduleId == request.ScheduleId, ct );
            if (alreadyLinked) {
                return Results.Conflict( new { message = "Schedule is already associated with this workflow." } );
            }

            WorkflowSchedule link = new( ) {
                WorkflowId = workflowId,
                ScheduleId = request.ScheduleId,
            };
            _ = dbContext.WorkflowSchedules.Add( link );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.Created( $"/api/workflows/{workflowId}/schedules", null );
        } )
        .WithName( "AssociateWorkflowSchedule" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapDelete( "/api/workflows/{workflowId}/schedules/{scheduleId}", async (
            long workflowId,
            Guid scheduleId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            WorkflowSchedule? link = await dbContext.WorkflowSchedules
                .FirstOrDefaultAsync(
                    ws => ws.WorkflowId == workflowId && ws.ScheduleId == scheduleId, ct );
            if (link is null) {
                return Results.NotFound( );
            }

            _ = dbContext.WorkflowSchedules.Remove( link );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.NoContent( );
        } )
        .WithName( "DisassociateWorkflowSchedule" )
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

        // Global workflow runs across all workflows (for the top-level Workflow Runs page)
        _ = app.MapGet( "/api/workflows/runs", async (
            int? limit,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            List<WorkflowRunDto> dtos = await dbContext.WorkflowRuns.AsNoTracking( )
                .Include( r => r.Workflow )
                .OrderByDescending( r => r.StartTime )
                .Take( limit ?? 50 )
                .Select( r => new WorkflowRunDto(
                    r.Id,
                    r.WorkflowId,
                    r.StartTime,
                    r.EndTime,
                    r.Status.ToString( ),
                    r.Workflow != null ? r.Workflow.Name : null ) )
                .ToListAsync( ct );
            return Results.Ok( dtos );
        } )
        .WithName( "GetAllWorkflowRuns" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/workflows/runs/{runId}", async (
            Guid runId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            WorkflowRun? run = await dbContext.WorkflowRuns.AsNoTracking( )
                .Include( r => r.Jobs )
                .Include( r => r.StepExecutions )
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

        // Step execution history for a run (all attempts)
        _ = app.MapGet( "/api/workflows/runs/{runId:guid}/step-executions", async (
            Guid runId,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            List<WorkflowStepExecution> executions = await dbContext.WorkflowStepExecutions
                .AsNoTracking( )
                .Where( e => e.WorkflowRunId == runId )
                .OrderBy( e => e.StepId )
                .ThenBy( e => e.Attempt )
                .ToListAsync( ct );
            return Results.Ok( executions.Select( WorkflowMapper.ToStepExecutionDto ) );
        } )
        .WithName( "GetStepExecutions" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/workflows/{id}/runs/{runId:guid}/retry-from/{stepId:long}", async (
            long id,
            Guid runId,
            long stepId,
            RetryFromFailedRequest? request,
            RetryFromFailedService retryService,
            ScheduleInvalidationDispatcher invalidationDispatcher,
            CancellationToken ct
        ) => {
            try {
                RetryFromFailedService.RetryResult result = await retryService.RetryAsync(
                    id, runId, stepId, request?.VariableOverrides, ct );

                await invalidationDispatcher.InvalidateAsync( result.ScheduleId, ct );

                return Results.Accepted( $"/api/workflows/runs/{runId}",
                    new { result.RunId, result.RetryFromStepId, result.ResetStepCount } );
            } catch (InvalidOperationException ex) {
                return Results.Conflict( new { message = ex.Message } );
            } catch (KeyNotFoundException ex) {
                return Results.NotFound( new { message = ex.Message } );
            }
        } )
        .WithName( "RetryFromFailed" )
        .RequireAuthorization( Policies.CanExecute );
    }

    // ── Workflow Dashboard ──

    /// <summary>
    /// Registers aggregated dashboard and latest-run-status endpoints.
    /// </summary>
    private static void MapWorkflowDashboard( WebApplication app ) {

        _ = app.MapGet( "/api/workflows/dashboard", async (
            int? page,
            int? pageSize,
            string? search,
            string? tag,
            string? status,
            bool? enabled,
            int? recentRunCount,
            WerkrDbContext dbContext,
            ScheduleService scheduleService,
            ILogger<Program> logger,
            CancellationToken ct
        ) => {
            int pageVal = Math.Max( 1, page ?? 1 );
            int pageSizeVal = Math.Clamp( pageSize ?? 25, 1, 100 );
            int sparklineCount = Math.Clamp( recentRunCount ?? 10, 1, 50 );

            // Query 1 — Paginated workflow summaries
            IQueryable<Workflow> query = dbContext.Workflows.AsNoTracking( );

            if (!string.IsNullOrWhiteSpace( search )) {
                query = query.Where( w => w.Name.Contains( search ) );
            }

            if (enabled.HasValue) {
                query = query.Where( w => w.Enabled == enabled.Value );
            }

            if (!string.IsNullOrWhiteSpace( tag )) {
                query = query.Where( w => w.TargetTags != null && w.TargetTags.Contains( tag ) );
            }

            bool hasStatusFilter = !string.IsNullOrWhiteSpace( status );
            int totalCount = await query.CountAsync( ct );

            IQueryable<Workflow> orderedQuery = query.OrderBy( w => w.Name );

            // When status filter is active, fetch all rows so we can filter
            // in memory before paginating; otherwise paginate at DB level.
            if (!hasStatusFilter) {
                orderedQuery = orderedQuery
                    .Skip( (pageVal - 1) * pageSizeVal )
                    .Take( pageSizeVal );
            }

            var summaries = await orderedQuery
                .Select( w => new {
                    w.Id,
                    w.Name,
                    w.Description,
                    w.Enabled,
                    w.TargetTags,
                    StepCount = w.Steps.Count,
                    RunCount = w.Runs.Count,
                    LastRun = w.Runs
                        .OrderByDescending( r => r.StartTime )
                        .Select( r => new { r.Id, r.Status, r.StartTime, r.EndTime } )
                        .FirstOrDefault( )
                } )
                .ToListAsync( ct );

            // Apply last-run status filter in memory (status is an enum stored as int)
            if (hasStatusFilter) {
                summaries = [.. summaries
                    .Where( s => s.LastRun is not null
                        && s.LastRun.Status.ToString( ).Equals( status, StringComparison.OrdinalIgnoreCase ) )];
                totalCount = summaries.Count;
                summaries = [.. summaries
                    .Skip( (pageVal - 1) * pageSizeVal )
                    .Take( pageSizeVal )];
            }

            List<long> workflowIds = [.. summaries.Select( w => w.Id )];

            // Query 2 — Sparkline data (recent runs for the page of workflow IDs)
            List<WorkflowRun> recentRuns;
            try {
                recentRuns = await dbContext.WorkflowRuns.AsNoTracking( )
                    .Where( r => workflowIds.Contains( r.WorkflowId ) )
                    .GroupBy( r => r.WorkflowId )
                    .SelectMany( g => g.OrderByDescending( r => r.StartTime ).Take( sparklineCount ) )
                    .ToListAsync( ct );
            } catch (Exception ex) {
                // Fallback: EF provider may not support GroupBy+SelectMany+Take
                logger.LogWarning( ex, "Sparkline GroupBy query failed; falling back to in-memory grouping" );
                recentRuns = await dbContext.WorkflowRuns.AsNoTracking( )
                    .Where( r => workflowIds.Contains( r.WorkflowId ) )
                    .OrderByDescending( r => r.StartTime )
                    .ToListAsync( ct );
                recentRuns = [.. recentRuns
                    .GroupBy( r => r.WorkflowId )
                    .SelectMany( g => g.Take( sparklineCount ) )];
            }

            ILookup<long, RunSparklineDto> sparklineByWorkflow = recentRuns
                .OrderBy( r => r.StartTime )
                .ToLookup(
                    r => r.WorkflowId,
                    r => new RunSparklineDto(
                        RunId: r.Id,
                        Status: r.Status.ToString( ),
                        DurationSeconds: r.EndTime.HasValue
                            ? ( r.EndTime.Value - r.StartTime ).TotalSeconds
                            : null ) );

            // Schedule computation — next scheduled run per workflow
            List<WorkflowSchedule> scheduleLinks = await dbContext.WorkflowSchedules.AsNoTracking( )
                .Where( ws => workflowIds.Contains( ws.WorkflowId ) )
                .ToListAsync( ct );

            Dictionary<long, DateTime?> nextScheduledByWorkflow = [];
            DateTime utcNow = DateTime.UtcNow;
            DateTime windowEnd = utcNow.AddDays( 30 );

            ILookup<long, Guid> scheduleIdsByWorkflow = scheduleLinks
                .ToLookup( ws => ws.WorkflowId, ws => ws.ScheduleId );

            foreach (long wfId in workflowIds) {
                DateTime? earliest = null;
                foreach (Guid scheduleId in scheduleIdsByWorkflow[wfId]) {
                    Schedule? schedule = await scheduleService.GetByIdAsync( scheduleId, ct );
                    if (schedule is null) {
                        continue;
                    }

                    try {
                        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences(
                            schedule, windowEnd );
                        DateTime? next = occurrences.FirstOrDefault( o => o > utcNow );
                        if (next.HasValue && next.Value != default
                            && (!earliest.HasValue || next.Value < earliest.Value)) {
                            earliest = next;
                        }
                    } catch {
                        // Schedule may be misconfigured — skip it
                    }
                }
                nextScheduledByWorkflow[wfId] = earliest;
            }

            // Assemble response
            List<WorkflowDashboardDto> items = [.. summaries.Select( s => new WorkflowDashboardDto(
                Id: s.Id,
                Name: s.Name,
                Description: s.Description,
                Enabled: s.Enabled,
                TargetTags: s.TargetTags,
                StepCount: s.StepCount,
                RunCount: s.RunCount,
                LastRun: s.LastRun is not null
                    ? new WorkflowRunSummaryDto(
                        Id: s.LastRun.Id,
                        Status: s.LastRun.Status.ToString( ),
                        StartTime: s.LastRun.StartTime,
                        EndTime: s.LastRun.EndTime )
                    : null,
                RecentRuns: [.. sparklineByWorkflow[s.Id]],
                NextScheduledRun: nextScheduledByWorkflow.GetValueOrDefault( s.Id )
            ) )];

            return Results.Ok( new WorkflowDashboardPageDto(
                Items: items,
                TotalCount: totalCount,
                Page: pageVal,
                PageSize: pageSizeVal ) );
        } )
        .WithName( "GetWorkflowDashboard" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/workflows/{id}/latest-run-status", async (
            long id,
            WerkrDbContext dbContext,
            ILogger<Program> logger,
            CancellationToken ct
        ) => {
            var latestRun = await dbContext.WorkflowRuns.AsNoTracking( )
                .Where( r => r.WorkflowId == id )
                .OrderByDescending( r => r.StartTime )
                .Select( r => new { r.Id, r.Status, r.StartTime, r.EndTime } )
                .FirstOrDefaultAsync( ct );

            if (latestRun is null) {
                return Results.Ok( new WorkflowLatestRunStatusDto(
                    RunId: null,
                    RunStatus: null,
                    RunStartTime: null,
                    RunEndTime: null,
                    Steps: [] ) );
            }

            List<StepStatusSummaryDto> steps;
            try {
                steps = await dbContext.WorkflowStepExecutions.AsNoTracking( )
                    .Where( e => e.WorkflowRunId == latestRun.Id )
                    .GroupBy( e => e.StepId )
                    .Select( g => g.OrderByDescending( e => e.Attempt ).First( ) )
                    .Select( e => new StepStatusSummaryDto(
                        StepId: e.StepId,
                        Status: e.Status.ToString( ),
                        StartTime: e.StartTime,
                        EndTime: e.EndTime,
                        ExitCode: e.Job != null ? e.Job.ExitCode : null ) )
                    .ToListAsync( ct );
            } catch (Exception ex) {
                // Fallback: EF provider (e.g. SQLite) may not support GroupBy+First
                logger.LogWarning( ex, "Latest-run-status GroupBy query failed; falling back to in-memory grouping" );
                List<WorkflowStepExecution> allExecs = await dbContext.WorkflowStepExecutions
                    .AsNoTracking( )
                    .Include( e => e.Job )
                    .Where( e => e.WorkflowRunId == latestRun.Id )
                    .OrderByDescending( e => e.Attempt )
                    .ToListAsync( ct );
                steps = [.. allExecs
                    .GroupBy( e => e.StepId )
                    .Select( g => g.First( ) )
                    .Select( e => new StepStatusSummaryDto(
                        StepId: e.StepId,
                        Status: e.Status.ToString( ),
                        StartTime: e.StartTime,
                        EndTime: e.EndTime,
                        ExitCode: e.Job?.ExitCode ) )];
            }

            return Results.Ok( new WorkflowLatestRunStatusDto(
                RunId: latestRun.Id,
                RunStatus: latestRun.Status.ToString( ),
                RunStartTime: latestRun.StartTime,
                RunEndTime: latestRun.EndTime,
                Steps: steps ) );
        } )
        .WithName( "GetLatestRunStatus" )
        .RequireAuthorization( Policies.CanRead );
    }

    [GeneratedRegex( @"<[^>]+>" )]
    private static partial Regex HtmlTagRegex( );

    [GeneratedRegex( @"^#[0-9a-fA-F]{6}$" )]
    private static partial Regex HexColorRegex( );

    private static string StripHtmlTags( string input ) =>
        string.IsNullOrEmpty( input ) ? input : HtmlTagRegex( ).Replace( input, string.Empty );
}
