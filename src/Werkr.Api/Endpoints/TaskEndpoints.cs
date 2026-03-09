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
        _ = app.MapGet( "/api/tasks", async (
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

        _ = app.MapGet( "/api/tasks/{id}", async (
            long id,
            TaskService taskService,
            CancellationToken ct
        ) => {
            WerkrTask? task = await taskService.GetByIdAsync( id, ct );
            return task is null ? Results.NotFound( ) : Results.Ok( TaskMapper.ToDto( task ) );
        } )
        .WithName( "GetTask" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost( "/api/tasks", async (
            TaskCreateRequest request,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                WerkrTask entity = TaskMapper.ToEntity( request );
                WerkrTask created = await taskService.CreateAsync( entity, ct );
                TaskDto dto = TaskMapper.ToDto( created );
                return Results.Created( $"/api/tasks/{dto.Id}", dto );
            } catch (System.ComponentModel.DataAnnotations.ValidationException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            } catch (Exception ex) when (ex is FormatException or ArgumentException) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "CreateTask" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapPut( "/api/tasks/{id}", async (
            long id,
            TaskUpdateRequest request,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                WerkrTask entity = TaskMapper.ToEntity( id, request );
                WerkrTask updated = await taskService.UpdateAsync( entity, ct );
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

        _ = app.MapDelete( "/api/tasks/{id}", async (
            long id,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                await taskService.DeleteAsync( id, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "DeleteTask" )
        .RequireAuthorization( Policies.CanDelete );

        _ = app.MapPut( "/api/tasks/{id}/enabled", async (
            long id,
            TaskSetEnabledRequest request,
            TaskService taskService,
            CancellationToken ct
        ) => {
            try {
                await taskService.SetEnabledAsync( id, request.Enabled, ct );
                return Results.NoContent( );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "SetTaskEnabled" )
        .RequireAuthorization( Policies.CanUpdate );

        // ── Run Now: creates a one-time schedule and invalidates agents ──
        _ = app.MapPost( "/api/tasks/{id}/run", async (
            long id,
            TaskRunRequest? request,
            RunNowService runNowService,
            ScheduleInvalidationDispatcher invalidationDispatcher,
            CancellationToken ct
        ) => {
            try {
                Guid scheduleId = await runNowService.CreateTaskRunNowAsync( id, ct );
                await invalidationDispatcher.InvalidateAsync( scheduleId, ct );
                return Results.Accepted( $"/api/tasks/{id}/latest-job",
                    new { scheduleId, message = "One-time schedule created. Execution will begin on the next agent sync." } );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            }
        } )
        .WithName( "RunTask" )
        .RequireAuthorization( Policies.CanExecute );

        // ── Latest Job: convenience endpoint for polling after Run Now ──
        _ = app.MapGet( "/api/tasks/{id}/latest-job", async (
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
