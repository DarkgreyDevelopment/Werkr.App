using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Triggers;
using Werkr.Data;
using Werkr.Data.Entities.Triggers;

namespace Werkr.Api.Endpoints;

/// <summary>
/// Extension methods for mapping file monitor trigger REST endpoints to the application.
/// </summary>
internal static class TriggerEndpoints {

    /// <summary>Maps all trigger-related REST endpoints.</summary>
    public static WebApplication MapTriggerEndpoints( this WebApplication app ) {
        MapFileMonitorCrud( app );
        return app;
    }

    /// <summary>
    /// Registers CRUD and enable/disable endpoints for file monitor triggers.
    /// </summary>
    private static void MapFileMonitorCrud( WebApplication app ) {

        // GET /api/v1/triggers/file-monitor — List all
        _ = app.MapGet( "/api/v1/triggers/file-monitor", async (
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            List<FileMonitorTriggerDto> triggers = await dbContext.FileMonitorTriggers
                .AsNoTracking( )
                .Include( t => t.Workflow )
                .OrderBy( t => t.Id )
                .Select( t => new FileMonitorTriggerDto(
                    t.Id,
                    t.WorkflowId,
                    t.Workflow.Name,
                    t.WatchDirectory,
                    t.FilePattern,
                    t.EventTypes,
                    t.DebounceMs,
                    t.Enabled,
                    t.TargetTags,
                    t.VersionBindingMode.ToString( ),
                    t.PinnedWorkflowVersionId ) )
                .ToListAsync( ct );
            return Results.Ok( triggers );
        } )
        .WithName( "GetFileMonitorTriggers" )
        .RequireAuthorization( Policies.CanRead );

        // GET /api/v1/triggers/file-monitor/{id} — Get by ID
        _ = app.MapGet( "/api/v1/triggers/file-monitor/{id}", async (
            long id,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            FileMonitorTrigger? trigger = await dbContext.FileMonitorTriggers
                .AsNoTracking( )
                .Include( t => t.Workflow )
                .FirstOrDefaultAsync( t => t.Id == id, ct );
            if (trigger is null) {
                return Results.NotFound( );
            }

            FileMonitorTriggerDto dto = new(
                trigger.Id,
                trigger.WorkflowId,
                trigger.Workflow.Name,
                trigger.WatchDirectory,
                trigger.FilePattern,
                trigger.EventTypes,
                trigger.DebounceMs,
                trigger.Enabled,
                trigger.TargetTags,
                trigger.VersionBindingMode.ToString( ),
                trigger.PinnedWorkflowVersionId );
            return Results.Ok( dto );
        } )
        .WithName( "GetFileMonitorTrigger" )
        .RequireAuthorization( Policies.CanRead );

        // POST /api/v1/triggers/file-monitor — Create
        _ = app.MapPost( "/api/v1/triggers/file-monitor", async (
            FileMonitorTriggerCreateRequest request,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            TriggerVersionService triggerVersionService,
            CancellationToken ct
        ) => {
            bool workflowExists = await dbContext.Workflows.AnyAsync(
                w => w.Id == request.WorkflowId, ct );
            if (!workflowExists) {
                return Results.NotFound( new { message = "Workflow not found." } );
            }

            // Validate EventTypes is valid JSON array
            if (!IsValidJsonArray( request.EventTypes )) {
                return Results.BadRequest( new { message = "EventTypes must be a valid JSON array of strings." } );
            }

            VersionBindingMode bindingMode = VersionBindingMode.Latest;
            if (request.VersionBindingMode is not null) {
                if (!Enum.TryParse( request.VersionBindingMode, ignoreCase: true, out bindingMode )) {
                    return Results.BadRequest( new { message = $"Invalid VersionBindingMode: '{request.VersionBindingMode}'. Valid values: Latest, Pinned." } );
                }
            }

            FileMonitorTrigger entity = new( ) {
                WorkflowId = request.WorkflowId,
                WatchDirectory = request.WatchDirectory,
                FilePattern = request.FilePattern ?? "*.*",
                EventTypes = request.EventTypes ?? "[\"created\"]",
                DebounceMs = request.DebounceMs ?? 500,
                Enabled = request.Enabled ?? true,
                TargetTags = request.TargetTags,
                VersionBindingMode = bindingMode,
                PinnedWorkflowVersionId = request.PinnedWorkflowVersionId,
            };

            _ = dbContext.FileMonitorTriggers.Add( entity );
            _ = await dbContext.SaveChangesAsync( ct );

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            _ = await triggerVersionService.CreateVersionAsync( entity, userId, "Initial version", ct );

            return Results.Created( $"/api/v1/triggers/file-monitor/{entity.Id}",
                new { entity.Id } );
        } )
        .WithName( "CreateFileMonitorTrigger" )
        .RequireAuthorization( Policies.CanCreate );

        // PUT /api/v1/triggers/file-monitor/{id} — Update
        _ = app.MapPut( "/api/v1/triggers/file-monitor/{id}", async (
            long id,
            FileMonitorTriggerUpdateRequest request,
            HttpContext httpContext,
            WerkrDbContext dbContext,
            TriggerVersionService triggerVersionService,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            FileMonitorTrigger? trigger = await dbContext.FileMonitorTriggers
                .FirstOrDefaultAsync( t => t.Id == id, ct );
            if (trigger is null) {
                return Results.NotFound( );
            }

            // Capture old binding values for audit
            VersionBindingMode oldBindingMode = trigger.VersionBindingMode;
            long? oldPinnedVersionId = trigger.PinnedWorkflowVersionId;

            if (request.WorkflowId.HasValue) {
                bool workflowExists = await dbContext.Workflows.AnyAsync(
                    w => w.Id == request.WorkflowId.Value, ct );
                if (!workflowExists) {
                    return Results.NotFound( new { message = "Workflow not found." } );
                }
                trigger.WorkflowId = request.WorkflowId.Value;
            }

            if (request.WatchDirectory is not null) {
                trigger.WatchDirectory = request.WatchDirectory;
            }

            if (request.FilePattern is not null) {
                trigger.FilePattern = request.FilePattern;
            }

            if (request.EventTypes is not null) {
                if (!IsValidJsonArray( request.EventTypes )) {
                    return Results.BadRequest( new { message = "EventTypes must be a valid JSON array of strings." } );
                }
                trigger.EventTypes = request.EventTypes;
            }

            if (request.DebounceMs.HasValue) {
                trigger.DebounceMs = request.DebounceMs.Value;
            }

            if (request.Enabled.HasValue) {
                trigger.Enabled = request.Enabled.Value;
            }

            if (request.TargetTags is not null) {
                trigger.TargetTags = request.TargetTags;
            }

            if (request.VersionBindingMode is not null) {
                if (!Enum.TryParse( request.VersionBindingMode, ignoreCase: true, out VersionBindingMode parsedMode )) {
                    return Results.BadRequest( new { message = $"Invalid VersionBindingMode: '{request.VersionBindingMode}'. Valid values: Latest, Pinned." } );
                }
                trigger.VersionBindingMode = parsedMode;
            }

            if (request.PinnedWorkflowVersionId.HasValue) {
                trigger.PinnedWorkflowVersionId = request.PinnedWorkflowVersionId;
            }

            _ = await dbContext.SaveChangesAsync( ct );

            string? userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value;
            _ = await triggerVersionService.CreateVersionAsync( trigger, userId, null, ct );

            // Emit audit event if binding mode or pinned version changed
            if (trigger.VersionBindingMode != oldBindingMode || trigger.PinnedWorkflowVersionId != oldPinnedVersionId) {
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.TriggerBindingUpdated.ToEventId( ),
                    ActorId: userId,
                    ActorType: userId is not null ? "user" : "system",
                    EntityType: "Trigger",
                    EntityId: trigger.Id.ToString( ),
                    ActionPerformed: "Binding updated",
                    Details: new {
                        TriggerId = trigger.Id,
                        OldBindingMode = oldBindingMode.ToString( ),
                        NewBindingMode = trigger.VersionBindingMode.ToString( ),
                        OldPinnedVersionId = oldPinnedVersionId,
                        NewPinnedVersionId = trigger.PinnedWorkflowVersionId,
                    }
                ), ct );
            }

            return Results.Ok( new { trigger.Id } );
        } )
        .WithName( "UpdateFileMonitorTrigger" )
        .RequireAuthorization( Policies.CanUpdate );

        // DELETE /api/v1/triggers/file-monitor/{id} — Delete
        _ = app.MapDelete( "/api/v1/triggers/file-monitor/{id}", async (
            long id,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            FileMonitorTrigger? trigger = await dbContext.FileMonitorTriggers
                .FirstOrDefaultAsync( t => t.Id == id, ct );
            if (trigger is null) {
                return Results.NotFound( );
            }

            _ = dbContext.FileMonitorTriggers.Remove( trigger );
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.NoContent( );
        } )
        .WithName( "DeleteFileMonitorTrigger" )
        .RequireAuthorization( Policies.CanDelete );

        // PATCH /api/v1/triggers/file-monitor/{id}/enabled — Enable/disable
        _ = app.MapPatch( "/api/v1/triggers/file-monitor/{id}/enabled", async (
            long id,
            FileMonitorTriggerSetEnabledRequest request,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            FileMonitorTrigger? trigger = await dbContext.FileMonitorTriggers
                .FirstOrDefaultAsync( t => t.Id == id, ct );
            if (trigger is null) {
                return Results.NotFound( );
            }

            trigger.Enabled = request.Enabled;
            _ = await dbContext.SaveChangesAsync( ct );
            return Results.Ok( new { trigger.Id, trigger.Enabled } );
        } )
        .WithName( "SetFileMonitorTriggerEnabled" )
        .RequireAuthorization( Policies.CanUpdate );
    }

    /// <summary>Validates that a string is a valid JSON array.</summary>
    private static bool IsValidJsonArray( string? value ) {
        if (string.IsNullOrWhiteSpace( value )) {
            return false;
        }

        try {
            using JsonDocument doc = JsonDocument.Parse( value );
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        } catch (JsonException) {
            return false;
        }
    }
}

// ── DTOs ──

/// <summary>DTO for file monitor trigger list/detail responses.</summary>
/// <param name="Id">Trigger ID.</param>
/// <param name="WorkflowId">Target workflow ID.</param>
/// <param name="WorkflowName">Target workflow name.</param>
/// <param name="WatchDirectory">Directory to watch.</param>
/// <param name="FilePattern">Glob pattern for file matching.</param>
/// <param name="EventTypes">JSON array of event types.</param>
/// <param name="DebounceMs">Debounce interval in milliseconds.</param>
/// <param name="Enabled">Whether the trigger is active.</param>
/// <param name="TargetTags">Optional JSON array of agent tags.</param>
/// <param name="VersionBindingMode">How this trigger resolves which workflow version to execute (Latest or Pinned).</param>
/// <param name="PinnedWorkflowVersionId">The pinned workflow version ID, if binding mode is Pinned.</param>
internal sealed record FileMonitorTriggerDto(
    long Id,
    long WorkflowId,
    string WorkflowName,
    string WatchDirectory,
    string FilePattern,
    string EventTypes,
    int DebounceMs,
    bool Enabled,
    string? TargetTags,
    string VersionBindingMode,
    long? PinnedWorkflowVersionId );

/// <summary>Request body for creating a file monitor trigger.</summary>
/// <param name="WorkflowId">Target workflow ID.</param>
/// <param name="WatchDirectory">Directory to watch.</param>
/// <param name="FilePattern">Optional glob pattern. Defaults to "*.*".</param>
/// <param name="EventTypes">Optional JSON array of event types. Defaults to ["created"].</param>
/// <param name="DebounceMs">Optional debounce interval. Defaults to 500.</param>
/// <param name="Enabled">Optional enabled flag. Defaults to true.</param>
/// <param name="TargetTags">Optional JSON array of agent tags.</param>
/// <param name="VersionBindingMode">Optional binding mode (Latest or Pinned). Defaults to Latest.</param>
/// <param name="PinnedWorkflowVersionId">Optional pinned workflow version ID.</param>
internal sealed record FileMonitorTriggerCreateRequest(
    long WorkflowId,
    string WatchDirectory,
    string? FilePattern = null,
    string? EventTypes = null,
    int? DebounceMs = null,
    bool? Enabled = null,
    string? TargetTags = null,
    string? VersionBindingMode = null,
    long? PinnedWorkflowVersionId = null );

/// <summary>Request body for updating a file monitor trigger.</summary>
/// <param name="WorkflowId">Optional new workflow ID.</param>
/// <param name="WatchDirectory">Optional new watch directory.</param>
/// <param name="FilePattern">Optional new file pattern.</param>
/// <param name="EventTypes">Optional new event types JSON array.</param>
/// <param name="DebounceMs">Optional new debounce interval.</param>
/// <param name="Enabled">Optional new enabled flag.</param>
/// <param name="TargetTags">Optional new target tags JSON.</param>
/// <param name="VersionBindingMode">Optional new binding mode (Latest or Pinned).</param>
/// <param name="PinnedWorkflowVersionId">Optional new pinned workflow version ID.</param>
internal sealed record FileMonitorTriggerUpdateRequest(
    long? WorkflowId = null,
    string? WatchDirectory = null,
    string? FilePattern = null,
    string? EventTypes = null,
    int? DebounceMs = null,
    bool? Enabled = null,
    string? TargetTags = null,
    string? VersionBindingMode = null,
    long? PinnedWorkflowVersionId = null );

/// <summary>Request body for enabling/disabling a file monitor trigger.</summary>
/// <param name="Enabled">Whether the trigger should be enabled.</param>
internal sealed record FileMonitorTriggerSetEnabledRequest( bool Enabled );
