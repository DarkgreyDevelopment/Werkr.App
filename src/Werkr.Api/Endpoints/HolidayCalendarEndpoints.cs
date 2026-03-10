using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Models;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models.Holidays;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Entities.Schedule;

namespace Werkr.Api.Endpoints;

/// <summary>Maps all holiday-calendar REST endpoints (calendar CRUD, rule CRUD, date CRUD, preview, attachment, audit).</summary>
internal static class HolidayCalendarEndpoints {

    /// <summary>Maps the 25 holiday-calendar endpoints.</summary>
    public static WebApplication MapHolidayCalendarEndpoints( this WebApplication app ) {

        // ── Calendar CRUD (6) ──────────────────────────────────────────────────

        // 1. GET /api/holiday-calendars
        _ = app.MapGet(
            "/api/holiday-calendars",
            async (
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                IReadOnlyList<HolidayCalendar> calendars = await service.GetAllAsync( ct );
                List<HolidayCalendarSummaryDto> dtos = [.. calendars.Select( c => HolidayCalendarMapper.ToSummaryDto( c, c.ScheduleLinks?.Count ?? 0 ) )];
                return Results.Ok( dtos );
            } )
        .WithName( "GetHolidayCalendars" )
        .RequireAuthorization( Policies.CanRead );

        // 2. GET /api/holiday-calendars/{id}
        _ = app.MapGet(
            "/api/holiday-calendars/{id}",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendar calendar = await service.GetByIdAsync( id, ct )
                        ?? throw new KeyNotFoundException( );
                    return Results.Ok( HolidayCalendarMapper.ToDto( calendar ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetHolidayCalendar" )
        .RequireAuthorization( Policies.CanRead );

        // 3. POST /api/holiday-calendars
        _ = app.MapPost(
            "/api/holiday-calendars",
            async (
                HolidayCalendarCreateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendar entity = HolidayCalendarMapper.ToEntity( request );
                    HolidayCalendar created = await service.CreateAsync( entity, ct );
                    HolidayCalendarDto dto = HolidayCalendarMapper.ToDto( created );
                    return Results.Created( $"/api/holiday-calendars/{dto.Id}", dto );
                } catch (ValidationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "CreateHolidayCalendar" )
        .RequireAuthorization( Policies.CanCreate );

        // 4. PUT /api/holiday-calendars/{id}
        _ = app.MapPut(
            "/api/holiday-calendars/{id}",
            async (
                Guid id,
                HolidayCalendarUpdateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendar existing = await service.GetByIdAsync( id, ct )
                        ?? throw new KeyNotFoundException( );
                    existing.Name = request.Name;
                    existing.Description = request.Description;
                    HolidayCalendar updated = await service.UpdateAsync( id, existing, ct );
                    return Results.Ok( HolidayCalendarMapper.ToDto( updated ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "UpdateHolidayCalendar" )
        .RequireAuthorization( Policies.CanUpdate );

        // 5. DELETE /api/holiday-calendars/{id}
        _ = app.MapDelete(
            "/api/holiday-calendars/{id}",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    await service.DeleteAsync( id, ct );
                    return Results.NoContent( );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "DeleteHolidayCalendar" )
        .RequireAuthorization( Policies.CanDelete );

        // 6. POST /api/holiday-calendars/{id}/clone
        _ = app.MapPost(
            "/api/holiday-calendars/{id}/clone",
            async (
                Guid id,
                CloneHolidayCalendarRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendar cloned = await service.CloneAsync( id, request.NewName, ct );
                    HolidayCalendarDto dto = HolidayCalendarMapper.ToDto( cloned );
                    return Results.Created( $"/api/holiday-calendars/{dto.Id}", dto );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "CloneHolidayCalendar" )
        .RequireAuthorization( Policies.CanCreate );

        // ── Rule CRUD (6) ──────────────────────────────────────────────────────

        // 7. GET /api/holiday-calendars/{id}/rules
        _ = app.MapGet(
            "/api/holiday-calendars/{id}/rules",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    IReadOnlyList<HolidayRule> rules = await service.GetRulesAsync( id, ct );
                    List<HolidayRuleDto> dtos = [.. rules.Select( HolidayCalendarMapper.ToDto )];
                    return Results.Ok( dtos );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetHolidayRules" )
        .RequireAuthorization( Policies.CanRead );

        // 8. GET /api/holiday-calendars/{id}/rules/{ruleId}
        _ = app.MapGet(
            "/api/holiday-calendars/{id}/rules/{ruleId}",
            async (
                Guid id,
                long ruleId,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayRule rule = await service.GetRuleAsync( id, ruleId, ct )
                        ?? throw new KeyNotFoundException( );
                    return Results.Ok( HolidayCalendarMapper.ToDto( rule ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetHolidayRule" )
        .RequireAuthorization( Policies.CanRead );

        // 9. POST /api/holiday-calendars/{id}/rules
        _ = app.MapPost(
            "/api/holiday-calendars/{id}/rules",
            async (
                Guid id,
                HolidayRuleCreateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayRule entity = HolidayCalendarMapper.ToEntity( request );
                    HolidayRule created = await service.AddRuleAsync( id, entity, ct );
                    HolidayRuleDto dto = HolidayCalendarMapper.ToDto( created );
                    return Results.Created( $"/api/holiday-calendars/{id}/rules/{dto.Id}", dto );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (ValidationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "CreateHolidayRule" )
        .RequireAuthorization( Policies.CanCreate );

        // 10. PUT /api/holiday-calendars/{id}/rules/{ruleId}
        _ = app.MapPut(
            "/api/holiday-calendars/{id}/rules/{ruleId}",
            async (
                Guid id,
                long ruleId,
                HolidayRuleUpdateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayRule entity = HolidayCalendarMapper.ToEntity( request );
                    HolidayRule updated = await service.UpdateRuleAsync( id, ruleId, entity, ct );
                    return Results.Ok( HolidayCalendarMapper.ToDto( updated ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (ValidationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "UpdateHolidayRule" )
        .RequireAuthorization( Policies.CanUpdate );

        // 11. DELETE /api/holiday-calendars/{id}/rules/{ruleId}
        _ = app.MapDelete(
            "/api/holiday-calendars/{id}/rules/{ruleId}",
            async (
                Guid id,
                long ruleId,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    await service.RemoveRuleAsync( id, ruleId, ct );
                    return Results.NoContent( );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "DeleteHolidayRule" )
        .RequireAuthorization( Policies.CanDelete );

        // 12. POST /api/holiday-calendars/rules/preview?startYear=&endYear=
        _ = app.MapPost(
            "/api/holiday-calendars/rules/preview",
            (
                int startYear,
                int endYear,
                RulePreviewRequest request
            ) => {
                HolidayRule rule = HolidayCalendarMapper.ToEntity( request );
                IReadOnlyList<HolidayDate> dates = HolidayCalculator.ComputeDatesForRange(
                    new HolidayCalendar { Rules = [rule] }, startYear, endYear );
                List<HolidayDateDto> dtos = [.. dates.Select( HolidayCalendarMapper.ToDto )];
                return Results.Ok( new RulePreviewResponse( startYear, endYear, dtos ) );
            } )
        .WithName( "PreviewHolidayRule" )
        .RequireAuthorization( Policies.CanRead );

        // ── Manual Date CRUD (6) ───────────────────────────────────────────────

        // 13. GET /api/holiday-calendars/{id}/dates
        _ = app.MapGet(
            "/api/holiday-calendars/{id}/dates",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    IReadOnlyList<HolidayDate> dates = await service.GetManualDatesAsync( id, ct );
                    List<HolidayDateDto> dtos = [.. dates.Select( HolidayCalendarMapper.ToDto )];
                    return Results.Ok( dtos );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetHolidayDates" )
        .RequireAuthorization( Policies.CanRead );

        // 14. GET /api/holiday-calendars/{id}/dates/{dateId}
        _ = app.MapGet(
            "/api/holiday-calendars/{id}/dates/{dateId}",
            async (
                Guid id,
                long dateId,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayDate date = await service.GetManualDateAsync( id, dateId, ct )
                        ?? throw new KeyNotFoundException( );
                    return Results.Ok( HolidayCalendarMapper.ToDto( date ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetHolidayDate" )
        .RequireAuthorization( Policies.CanRead );

        // 15. POST /api/holiday-calendars/{id}/dates
        _ = app.MapPost(
            "/api/holiday-calendars/{id}/dates",
            async (
                Guid id,
                HolidayDateCreateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayDate entity = HolidayCalendarMapper.ToEntity( request );
                    HolidayDate created = await service.AddManualDateAsync( id, entity, ct );
                    HolidayDateDto dto = HolidayCalendarMapper.ToDto( created );
                    return Results.Created( $"/api/holiday-calendars/{id}/dates/{dto.Id}", dto );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "CreateHolidayDate" )
        .RequireAuthorization( Policies.CanCreate );

        // 16. PUT /api/holiday-calendars/{id}/dates/{dateId}
        _ = app.MapPut(
            "/api/holiday-calendars/{id}/dates/{dateId}",
            async (
                Guid id,
                long dateId,
                HolidayDateCreateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayDate entity = HolidayCalendarMapper.ToEntity( request );
                    HolidayDate updated = await service.UpdateManualDateAsync( id, dateId, entity, ct );
                    return Results.Ok( HolidayCalendarMapper.ToDto( updated ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "UpdateHolidayDate" )
        .RequireAuthorization( Policies.CanUpdate );

        // 17. DELETE /api/holiday-calendars/{id}/dates/{dateId}
        _ = app.MapDelete(
            "/api/holiday-calendars/{id}/dates/{dateId}",
            async (
                Guid id,
                long dateId,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    await service.RemoveManualDateAsync( id, dateId, ct );
                    return Results.NoContent( );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "DeleteHolidayDate" )
        .RequireAuthorization( Policies.CanDelete );

        // 18. POST /api/holiday-calendars/{id}/dates/bulk
        _ = app.MapPost(
            "/api/holiday-calendars/{id}/dates/bulk",
            async (
                Guid id,
                BulkHolidayDateCreateRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    List<HolidayDate> entities = [.. request.Dates.Select( HolidayCalendarMapper.ToEntity )];
                    IReadOnlyList<HolidayDate> created = await service.BulkAddManualDatesAsync( id, entities, ct );
                    List<HolidayDateDto> dtos = [.. created.Select( HolidayCalendarMapper.ToDto )];
                    return Results.Created( $"/api/holiday-calendars/{id}/dates", dtos );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                } catch (InvalidOperationException ex) {
                    return Results.BadRequest( new { message = ex.Message } );
                }
            } )
        .WithName( "BulkCreateHolidayDates" )
        .RequireAuthorization( Policies.CanCreate );

        // ── Calendar Preview (1) ───────────────────────────────────────────────

        // 19. GET /api/holiday-calendars/{id}/preview?startYear=&endYear=
        _ = app.MapGet(
            "/api/holiday-calendars/{id}/preview",
            async (
                Guid id,
                int startYear,
                int endYear,
                HolidayCalendarService service,
                HolidayDateService dateService,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendar? cal = await service.GetByIdAsync( id, ct );
                    if (cal is null) {
                        return Results.NotFound( );
                    }

                    IReadOnlyList<HolidayDate> dates = await dateService.GetDatesForRangeAsync(
                        id,
                        new DateOnly( startYear, 1, 1 ),
                        new DateOnly( endYear, 12, 31 ),
                        ct );
                    List<HolidayDateDto> dtos = [.. dates.Select( HolidayCalendarMapper.ToDto )];
                    return Results.Ok( new HolidayPreviewResponse( id, startYear, endYear, dtos ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "PreviewHolidayCalendar" )
        .RequireAuthorization( Policies.CanRead );

        // ── Schedule Attachment (3) ────────────────────────────────────────────

        // 20. GET /api/schedules/{id}/holiday-calendar
        _ = app.MapGet(
            "/api/schedules/{id}/holiday-calendar",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    ScheduleHolidayCalendar? link = await service.GetScheduleCalendarAsync( id, ct );
                    if (link is null) {
                        return Results.NoContent( );
                    }

                    HolidayCalendar? calendar = await service.GetByIdAsync( link.HolidayCalendarId, ct );
                    string calName = calendar?.Name ?? "Unknown";
                    return Results.Ok( new ScheduleHolidayCalendarDto(
                        link.HolidayCalendarId, calName, link.Mode.ToString( ) ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetScheduleHolidayCalendar" )
        .RequireAuthorization( Policies.CanRead );

        // 21. PUT /api/schedules/{id}/holiday-calendar
        _ = app.MapPut(
            "/api/schedules/{id}/holiday-calendar",
            async (
                Guid id,
                AttachHolidayCalendarRequest request,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    HolidayCalendarMode mode = Enum.Parse<HolidayCalendarMode>( request.Mode, ignoreCase: true );
                    _ = await service.AttachToScheduleAsync( id, request.CalendarId, mode, ct );

                    HolidayCalendar? calendar = await service.GetByIdAsync( request.CalendarId, ct );
                    string calName = calendar?.Name ?? "Unknown";
                    return Results.Ok( new ScheduleHolidayCalendarDto(
                        request.CalendarId, calName, mode.ToString( ) ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "AttachHolidayCalendar" )
        .RequireAuthorization( Policies.CanUpdate );

        // 22. DELETE /api/schedules/{id}/holiday-calendar
        _ = app.MapDelete(
            "/api/schedules/{id}/holiday-calendar",
            async (
                Guid id,
                HolidayCalendarService service,
                CancellationToken ct
            ) => {
                try {
                    await service.DetachFromScheduleAsync( id, ct );
                    return Results.NoContent( );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "DetachHolidayCalendar" )
        .RequireAuthorization( Policies.CanUpdate );

        // ── Agent Holiday Data (1) ─────────────────────────────────────────────

        // 23. GET /api/schedules/{id}/holiday-dates?start=&end=
        _ = app.MapGet(
            "/api/schedules/{id}/holiday-dates",
            async (
                Guid id,
                DateOnly start,
                DateOnly end,
                HolidayCalendarService calService,
                HolidayDateService dateService,
                CancellationToken ct
            ) => {
                try {
                    ScheduleHolidayCalendar? link = await calService.GetScheduleCalendarAsync( id, ct );
                    if (link is null) {
                        return Results.NoContent( );
                    }

                    IReadOnlyList<HolidayDate> dates = await dateService.GetDatesForRangeAsync(
                        link.HolidayCalendarId, start, end, ct );
                    List<HolidayDateDto> dtos = [.. dates.Select( HolidayCalendarMapper.ToDto )];
                    return Results.Ok( new ScheduleHolidayDatesResponse(
                        id, link.HolidayCalendarId, link.Mode.ToString( ), dtos ) );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "GetScheduleHolidayDates" )
        .RequireAuthorization( Policies.CanRead );

        // ── Audit Log (2) ──────────────────────────────────────────────────────

        // 24. POST /api/schedules/{id}/audit-log
        _ = app.MapPost(
            "/api/schedules/{id}/audit-log",
            async (
                Guid id,
                ScheduleAuditLogCreateRequest request,
                HolidayCalendarService calService,
                WerkrDbContext db,
                CancellationToken ct
            ) => {
                try {
                    ScheduleHolidayCalendar? link = await calService.GetScheduleCalendarAsync( id, ct );
                    if (link is null) {
                        return Results.BadRequest( new { message = "No holiday calendar attached." } );
                    }

                    HolidayCalendar? calendar = await calService.GetByIdAsync( link.HolidayCalendarId, ct );
                    string calName = calendar?.Name ?? "Unknown";

                    ScheduleAuditLog log = HolidayCalendarMapper.ToAuditLog( request, id, calName, link.Mode );
                    _ = db.ScheduleAuditLogs.Add( log );
                    _ = await db.SaveChangesAsync( ct );

                    ScheduleAuditLogDto dto = HolidayCalendarMapper.ToDto( log );
                    return Results.Created( $"/api/schedules/{id}/audit-log", dto );
                } catch (KeyNotFoundException) {
                    return Results.NotFound( );
                }
            } )
        .WithName( "CreateScheduleAuditLog" )
        .RequireAuthorization( Policies.CanCreate );

        // 25. GET /api/schedules/{id}/audit-log?from=&to=
        _ = app.MapGet(
            "/api/schedules/{id}/audit-log",
            async (
                Guid id,
                DateTime from,
                DateTime to,
                WerkrDbContext db,
                CancellationToken ct
            ) => {
                List<ScheduleAuditLog> logs = await db.ScheduleAuditLogs
                    .Where( l => l.ScheduleId == id && l.CreatedUtc >= from && l.CreatedUtc <= to )
                    .OrderByDescending( l => l.CreatedUtc )
                    .ToListAsync( ct );
                List<ScheduleAuditLogDto> dtos = [.. logs.Select( HolidayCalendarMapper.ToDto )];
                return Results.Ok( dtos );
            } )
        .WithName( "GetScheduleAuditLog" )
        .RequireAuthorization( Policies.CanRead );

        return app;
    }
}
