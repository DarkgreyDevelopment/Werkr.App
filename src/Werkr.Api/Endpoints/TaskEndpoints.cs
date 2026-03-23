using System.Security.Claims;
using Werkr.Api.Models;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Core.Scheduling;
using Werkr.Core.Tasks;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Endpoints;

/// <summary>Maps all task-related REST endpoints.</summary>
internal static class TaskEndpoints {
    /// <summary>Maps task CRUD, enabled-toggle, and run endpoints.</summary>
    public static WebApplication MapTaskEndpoints( this WebApplication app ) {
        _ = app.MapGet( "/api/v1/tasks", async (
            long? workflowId,
            TaskService taskService,
            CancellationToken ct
        ) => {
            IReadOnlyList<WerkrTask> tasks = await taskService.GetAllAsync( workflowId, ct );
            List<TaskDto> dtos = [.. tasks.Select( TaskMapper.ToDto )];
            return Results.Ok( dtos );
        } )
        .WithName( "GetTasks" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/v1/tasks/{id}", async (
            long id,
            TaskService taskService,
            CancellationToken ct
        ) => {
            WerkrTask? task = await taskService.GetByIdAsync( id, ct );
            return task is null ? Results.NotFound( ) : Results.Ok( TaskMapper.ToDto( task ) );
        } )
        .WithName( "GetTask" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/v1/tasks", async (
            TaskCreateRequest request,
            HttpContext httpContext,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                WerkrTask entity = TaskMapper.ToEntity( request );
                WerkrTask created = await taskService.CreateAsync( entity, userId, ct );
                TaskDto dto = TaskMapper.ToDto( created );
                return Results.Created( $"/api/v1/tasks/{dto.Id}", dto );
            } catch (System.ComponentModel.DataAnnotations.ValidationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "CreateTask" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapPut( "/api/v1/tasks/{id}", async (
            long id,
            TaskUpdateRequest request,
            HttpContext httpContext,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                WerkrTask entity = TaskMapper.ToEntity( id, request );
                WerkrTask updated = await taskService.UpdateAsync( entity, userId, request.ChangeDescription, ct );
                return Results.Ok( TaskMapper.ToDto( updated ) );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (System.ComponentModel.DataAnnotations.ValidationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "UpdateTask" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapDelete( "/api/v1/tasks/{id}", async (
            long id,
            HttpContext httpContext,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await taskService.DeleteAsync( id, userId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "DeleteTask" )
        .RequireAuthorization( Policies.CanDelete );

        _ = app.MapPut( "/api/v1/tasks/{id}/enabled", async (
            long id,
            TaskSetEnabledRequest request,
            HttpContext httpContext,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await taskService.SetEnabledAsync( id, request.Enabled, userId, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "SetTaskEnabled" )
        .RequireAuthorization( Policies.CanUpdate );

        // ── Run Now: creates a one-time schedule and invalidates agents ──
        _ = app.MapPost( "/api/v1/tasks/{id}/run", async (
            long id,
            TaskRunRequest? request,
            RunNowService runNowService,
            ScheduleInvalidationDispatcher invalidationDispatcher,
            CancellationToken ct
        ) => {
            try {
                Guid scheduleId = await runNowService.CreateTaskRunNowAsync( id, ct );
                await invalidationDispatcher.InvalidateAsync( scheduleId, ct );
                return Results.Accepted( $"/api/v1/tasks/{id}/latest-job",
                    new { scheduleId, message = "One-time schedule created. Execution will begin on the next agent sync." } );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "RunTask" )
        .RequireAuthorization( Policies.CanExecute );

        // ── Latest Job: convenience endpoint for polling after Run Now ──
        _ = app.MapGet( "/api/v1/tasks/{id}/latest-job", async (
            long id,
            JobExecutionService jobService,
            CancellationToken ct
        ) => {
            IReadOnlyList<WerkrJob> jobs = await jobService.GetJobHistoryAsync( id, limit: 1, ct );
            return jobs.Count == 0 ? Results.NotFound( ) : Results.Ok( jobs[0] );
        } )
        .WithName( "GetLatestJobForTask" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
