using Werkr.Api.Models;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Models.Holidays;
using Werkr.Core.Scheduling;
using Werkr.Data.Calendar.Models;

namespace Werkr.Api.Endpoints;

/// <summary>Maps all schedule-related REST endpoints.</summary>
internal static class ScheduleEndpoints {
    /// <summary>Maps schedule CRUD + occurrence-preview endpoints.</summary>
    public static WebApplication MapScheduleEndpoints( this WebApplication app ) {
        _ = app.MapGet(
            "/api/v1/schedules",
            async (
                ScheduleService scheduleService,
                CancellationToken ct
            ) => {
                IReadOnlyList<Schedule> schedules = await scheduleService.GetAllAsync( ct );
                List<ScheduleDto> dtos = [.. schedules.Select( ScheduleMapper.ToDto )];
                return Results.Ok( dtos );
            } )
        .WithName( "GetSchedules" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/v1/schedules/{id}",
            async (
                Guid id,
                ScheduleService scheduleService,
                CancellationToken ct
            ) => {
                Schedule? schedule = await scheduleService.GetByIdAsync( id, ct );
                return schedule is null
                    ? Results.NotFound( )
                    : Results.Ok( ScheduleMapper.ToDto( schedule ) );
            } )
        .WithName( "GetSchedule" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPost(
            "/api/v1/schedules",
            async (
                ScheduleCreateRequest request,
                ScheduleService scheduleService,
                CancellationToken ct
            ) => {
                try {
                    Schedule schedule = ScheduleMapper.ToSchedule( request );
                    Schedule created = await scheduleService.CreateAsync( schedule, ct );
                    ScheduleDto dto = ScheduleMapper.ToDto( created );
                    return Results.Created( $"/api/v1/schedules/{dto.Id}", dto );
                } catch (System.ComponentModel.DataAnnotations.ValidationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                } catch (TimeZoneNotFoundException ex) {
                    return Results.BadRequest( new { message = $"Invalid timezone: {ex.Message}" } );
                }
            } )
        .WithName( "CreateSchedule" )
        .RequireAuthorization( Policies.CanCreate );

        _ = app.MapPut(
            "/api/v1/schedules/{id}",
            async (
                Guid id,
                ScheduleUpdateRequest request,
                ScheduleService scheduleService,
                ScheduleInvalidationDispatcher invalidationDispatcher,
                CancellationToken ct
            ) => {
                try {
                    Schedule schedule = ScheduleMapper.ToSchedule( id, request );
                    Schedule updated = await scheduleService.UpdateAsync( schedule, ct );

                    // Push invalidation to affected agents (fire-and-forget)
                    _ = Task.Run( async ( ) => {
                        try {
                            await invalidationDispatcher.InvalidateAsync( id, CancellationToken.None );
                        } catch (Exception ex) {
                            app.Logger.LogError( ex, "Schedule invalidation failed for {ScheduleId}.", id );
                        }
                    }, CancellationToken.None );

                    return Results.Ok( ScheduleMapper.ToDto( updated ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (System.ComponentModel.DataAnnotations.ValidationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                } catch (TimeZoneNotFoundException ex) {
                    return Results.BadRequest( new { message = $"Invalid timezone: {ex.Message}" } );
                }
            } )
        .WithName( "UpdateSchedule" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapDelete(
            "/api/v1/schedules/{id}",
            async (
                Guid id,
                ScheduleService scheduleService,
                ScheduleInvalidationDispatcher invalidationDispatcher,
                CancellationToken ct
            ) => {
                try {
                    // Push invalidation BEFORE deleting so we can still find affected tasks
                    await invalidationDispatcher.InvalidateAsync( id, ct );

                    await scheduleService.DeleteAsync( id, ct );
                    return Results.NoContent( );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "DeleteSchedule" )
        .RequireAuthorization( Policies.CanDelete );

        _ = app.MapGet(
            "/api/v1/schedules/{id}/occurrences",
            async (
                Guid id,
                DateTime windowEnd,
                ScheduleService scheduleService,
                CancellationToken ct
            ) => {
                try {
                    ScheduleOccurrenceResult result = await scheduleService.PreviewOccurrencesAsync( id, windowEnd, ct );
                    IReadOnlyList<SuppressedOccurrenceDto>? suppressed = result.Suppressed.Count > 0
                        ? result.Suppressed.Select( s => new SuppressedOccurrenceDto( s.UtcTime, s.HolidayName, s.Reason ) ).ToList( )
                        : null;

                    // Load schedule to get calendar info for the response
                    Schedule? schedule = await scheduleService.GetByIdAsync( id, ct );
                    string? calendarName = schedule?.HolidayCalendar?.Name;
                    string? calendarMode = schedule?.HolidayCalendarMode?.ToString( );

                    OccurrencePreviewResponse response = new(
                        id, windowEnd, result.Occurrences, suppressed, calendarName, calendarMode );
                    return Results.Ok( response );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "PreviewOccurrences" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
