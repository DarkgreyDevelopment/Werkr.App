using Microsoft.Extensions.Options;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data.Entities.Audit;

namespace Werkr.Api.Endpoints;

/// <summary>Maps all audit event REST endpoints. All require <see cref="Policies.IsAdmin"/>.</summary>
internal static class AuditEndpoints {
    /// <summary>Maps audit query, create, export, and registry endpoints.</summary>
    public static WebApplication MapAuditEndpoints( this WebApplication app ) {

        // GET /api/v1/audit — paginated query with filter query parameters
        _ = app.MapGet(
            "/api/v1/audit",
            async (
                string? eventTypeId,
                string? eventCategory,
                string? sourceModule,
                string? actorId,
                string? actorType,
                string? entityType,
                string? entityId,
                string? correlationId,
                DateTime? fromUtc,
                DateTime? toUtc,
                int? limit,
                int? offset,
                IAuditService auditService,
                CancellationToken ct
            ) => {
                int effectiveLimit = limit ?? 50;
                int effectiveOffset = offset ?? 0;

                if (effectiveLimit is < 1 or > 200) {
                    return Results.BadRequest( new { message = "Limit must be between 1 and 200." } );
                }
                if (effectiveOffset < 0) {
                    return Results.BadRequest( new { message = "Offset must be non-negative." } );
                }

                AuditQuery query = new( ) {
                    EventTypeId = eventTypeId,
                    EventCategory = eventCategory,
                    SourceModule = sourceModule,
                    ActorId = actorId,
                    ActorType = actorType,
                    EntityType = entityType,
                    EntityId = entityId,
                    CorrelationId = correlationId,
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    Limit = effectiveLimit,
                    Offset = effectiveOffset
                };

                PagedResult<AuditEventDto> result = await auditService.QueryAsync( query, ct );
                return Results.Ok( result );
            } )
        .WithName( "QueryAuditEvents" )
        .RequireAuthorization( Policies.IsAdmin );

        // POST /api/v1/audit — create audit event (used by Server)
        _ = app.MapPost(
            "/api/v1/audit",
            async (
                AuditEntry request,
                IAuditService auditService,
                IAuditEventTypeRegistry registry,
                CancellationToken ct
            ) => {
                if (registry.GetByTypeId( request.EventTypeId ) is null) {
                    return Results.BadRequest( new { message = $"Unknown event type: '{request.EventTypeId}'." } );
                }

                if (!Enum.TryParse<ActorType>( request.ActorType, ignoreCase: true, out _ )) {
                    return Results.BadRequest( new { message = $"Invalid actor type: '{request.ActorType}'." } );
                }

                await auditService.LogAsync( request, ct );
                return Results.Created( "/api/v1/audit", null );
            } )
        .WithName( "CreateAuditEvent" )
        .RequireAuthorization( Policies.IsAdmin );

        // GET /api/v1/audit/event-types — list all registered types grouped by category
        _ = app.MapGet(
            "/api/v1/audit/event-types",
            (
                IAuditEventTypeRegistry registry
            ) => {
                IReadOnlyList<AuditEventTypeDto> types = registry.GetAll( );
                Dictionary<string, List<AuditEventTypeDto>> grouped = [];
                foreach (AuditEventTypeDto t in types) {
                    if (!grouped.TryGetValue( t.Category, out List<AuditEventTypeDto>? list )) {
                        list = [];
                        grouped[t.Category] = list;
                    }
                    list.Add( t );
                }
                return Results.Ok( grouped );
            } )
        .WithName( "GetAuditEventTypes" )
        .RequireAuthorization( Policies.IsAdmin );

        // GET /api/v1/audit/categories — list registered categories
        _ = app.MapGet(
            "/api/v1/audit/categories",
            (
                IAuditEventTypeRegistry registry
            ) => Results.Ok( registry.GetCategories( ) ) )
        .WithName( "GetAuditCategories" )
        .RequireAuthorization( Policies.IsAdmin );

        // GET /api/v1/audit/modules — list registered modules
        _ = app.MapGet(
            "/api/v1/audit/modules",
            (
                IAuditEventTypeRegistry registry
            ) => Results.Ok( registry.GetModules( ) ) )
        .WithName( "GetAuditModules" )
        .RequireAuthorization( Policies.IsAdmin );

        // GET /api/v1/audit/entity-types — list distinct entity types from audit data
        _ = app.MapGet(
            "/api/v1/audit/entity-types",
            async (
                IAuditService auditService,
                CancellationToken ct
            ) => Results.Ok( await auditService.GetEntityTypesAsync( ct ) ) )
        .WithName( "GetAuditEntityTypes" )
        .RequireAuthorization( Policies.IsAdmin );

        // POST /api/v1/audit/export — streaming file download (JSON or CSV)
        _ = app.MapPost(
            "/api/v1/audit/export",
            async (
                AuditExportRequest request,
                IAuditService auditService,
                IOptions<AuditLogOptions> auditLogOptions,
                CancellationToken ct
            ) => {
                if (!Enum.IsDefined( request.Format )) {
                    return Results.BadRequest( new { message = $"Invalid export format: '{request.Format}'." } );
                }

                int maxRows = auditLogOptions.Value.MaxExportRows;
                string ext = request.Format == ExportFormat.Csv ? "csv" : "json";
                string contentType = request.Format == ExportFormat.Csv ? "text/csv" : "application/json";
                string fileName = $"audit-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{ext}";

                return Results.Stream(
                    async stream => await auditService.ExportAsync( request.Query, request.Format, stream, ct, maxRows ),
                    contentType: contentType,
                    fileDownloadName: fileName );
            } )
        .WithName( "ExportAuditEvents" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
