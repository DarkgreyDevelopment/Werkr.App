using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Werkr.Api.Services;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

// KeyRotationService used for POST /api/agents/{id}/rotate-key

namespace Werkr.Api.Endpoints;

/// <summary>Maps all agent-related REST endpoints.</summary>
internal static class AgentEndpoints {
    /// <summary>Maps agent CRUD, health, activity, execute, tags, connection, and key rotation endpoints.</summary>
    public static WebApplication MapAgentEndpoints( this WebApplication app ) {
        MapAgentCrud( app );
        MapAgentHealth( app );
        MapAgentActivity( app );
        MapAgentExecute( app );
        MapAgentTags( app );
        MapAgentConnections( app );
        MapAgentKeyRotation( app );
        return app;
    }

    // ── Agent CRUD ──

    /// <summary>
    /// Registers CRUD and status-management endpoints for agent connections.
    /// </summary>
    private static void MapAgentCrud( WebApplication app ) {
        _ = app.MapGet(
            "/api/v1/agents",
            async (
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                List<RegisteredConnection> connections = await dbContext.RegisteredConnections
                    .Where(c => c.IsServer)
                    .OrderByDescending(c => c.Created)
                    .ToListAsync(ct);

                List<AgentListDto> agents = [.. connections.Select( c => new AgentListDto(
                    c.Id, c.ConnectionName, c.RemoteUrl, c.Status.ToString( ),
                    c.LastSeen, c.Created,
                    string.IsNullOrEmpty( c.AgentVersion ) ? null : c.AgentVersion ) )];

                return Results.Ok( agents );
            } )
        .WithName( "GetAgents" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/v1/agents/{id}",
            async (
                Guid id,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );

                if (connection is null) {
                    return Results.NotFound( );
                }

                byte[] remotePublicKeyBytes = EncryptionProvider.SerializePublicKey( connection.RemotePublicKey );
                string keyString = System.Text.Encoding.UTF8.GetString( remotePublicKeyBytes );
                string fingerprint = EncryptionProvider.ComputeKeyFingerprint( keyString );

                AgentDetailDto dto = new(
                    connection.Id,
                    connection.ConnectionName,
                    connection.RemoteUrl,
                    connection.Status.ToString( ),
                    fingerprint,
                    connection.Created,
                    connection.LastSeen,
                    null,
                    null,
                    string.IsNullOrEmpty( connection.AgentVersion ) ? null : connection.AgentVersion
                );

                return Results.Ok( dto );
            } )
        .WithName( "GetAgentDetail" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPut(
            "/api/v1/agents/{id}",
            async (
                Guid id,
                UpdateAgentRequest request,
                ClaimsPrincipal user,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
                IAuditService auditService,
                CancellationToken ct
            ) => {
                if (string.IsNullOrWhiteSpace( request.ConnectionName )
                    && string.IsNullOrWhiteSpace( request.RemoteUrl )) {
                    return Results.BadRequest( "At least one of ConnectionName or RemoteUrl must be provided." );
                }

                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );
                if (connection is null) {
                    return Results.NotFound( );
                }

                if (!string.IsNullOrWhiteSpace( request.ConnectionName )) {
                    string trimmed = request.ConnectionName.Trim( );
                    if (trimmed.Length > 200) {
                        return Results.BadRequest( "Connection name must be 200 characters or fewer." );
                    }
                    connection.ConnectionName = trimmed;
                }

                if (!string.IsNullOrWhiteSpace( request.RemoteUrl )) {
                    string urlTrimmed = request.RemoteUrl.Trim( );

                    if (!Uri.TryCreate( urlTrimmed, UriKind.Absolute, out Uri? parsedUri )
                        || (parsedUri.Scheme != "https" && parsedUri.Scheme != "http")) {
                        return Results.BadRequest( "RemoteUrl must be a valid HTTP or HTTPS URL." );
                    }

                    string oldUrl = connection.RemoteUrl;
                    connection.RemoteUrl = urlTrimmed;

                    // If the URL changed, remove the cached gRPC channel so it reconnects
                    if (!string.Equals( oldUrl, urlTrimmed, StringComparison.OrdinalIgnoreCase )) {
                        connectionManager.RemoveChannel( id );
                    }
                }

                _ = await dbContext.SaveChangesAsync( ct );

                // Audit: agent updated
                string? userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentUpdated.ToEventId( ),
                    ActorId: userId, ActorType: "User",
                    EntityType: "Agent", EntityId: id.ToString( ),
                    ActionPerformed: "Updated",
                    Details: new { ConnectionName = connection.ConnectionName, RemoteUrl = connection.RemoteUrl }
                ), ct );

                return Results.NoContent( );
            } )
        .WithName( "UpdateAgent" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapPost(
            "/api/v1/agents/{id}/revoke",
            async (
                Guid id,
                ClaimsPrincipal user,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
                IAuditService auditService,
                CancellationToken ct
            ) => {
                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .FirstOrDefaultAsync( c => c.Id == id, ct );

                if (connection is null) {
                    return Results.NotFound( new { message = "Connection not found." } );
                }

                connection.Status = ConnectionStatus.Revoked;
                _ = await dbContext.SaveChangesAsync( ct );
                connectionManager.RemoveChannel( id );

                // Audit: agent revoked
                string? userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentRevoked.ToEventId( ),
                    ActorId: userId,
                    ActorType: "User",
                    EntityType: "Agent",
                    EntityId: id.ToString( ),
                    ActionPerformed: "Revoked",
                    Details: new { AgentName = connection.ConnectionName }
                ), ct );

                return Results.Ok( new { message = $"Connection '{connection.ConnectionName}' revoked." } );
            } )
        .WithName( "RevokeAgent" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut(
            "/api/v1/agents/{id}/status",
            async (
                Guid id,
                UpdateAgentStatusRequest request,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                if (!Enum.TryParse<ConnectionStatus>(
                    request.Status,
                    ignoreCase: true,
                    out ConnectionStatus newStatus
                )) {
                    return Results.BadRequest( new { message = $"Invalid status: {request.Status}" } );
                }

                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );
                if (connection is null) {
                    return Results.NotFound( );
                }

                // Do not overwrite Revoked status — that requires explicit admin action via /revoke
                if (connection.Status == ConnectionStatus.Revoked) {
                    return Results.Ok( new { status = connection.Status.ToString( ) } );
                }

                connection.Status = newStatus;
                if (newStatus == ConnectionStatus.Connected) {
                    connection.LastSeen = DateTime.UtcNow;
                }
                _ = await dbContext.SaveChangesAsync( ct );
                return Results.Ok( new { status = connection.Status.ToString( ) } );
            } )
        .WithName( "UpdateAgentStatus" )
        .RequireAuthorization( Policies.CanUpdate );
    }

    // ── Agent Health ──

    /// <summary>
    /// Registers the aggregate agent health endpoint backed by stored LastSeen/Status data.
    /// </summary>
    private static void MapAgentHealth( WebApplication app ) {
        const int OfflineThresholdSeconds = 180;

        _ = app.MapGet(
            "/api/v1/agents/health",
            async (
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                List<RegisteredConnection> connections = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer )
                    .OrderBy( c => c.ConnectionName )
                    .ToListAsync( ct );

                DateTime now = DateTime.UtcNow;
                DateTime cutoff = now.AddSeconds( -OfflineThresholdSeconds );

                List<AgentHealthDto> results = [.. connections.Select( c => {
                    // Revoked agents always show as Revoked
                    // Connected agents whose LastSeen is stale are shown as Unreachable
                    string status = c.Status switch {
                        ConnectionStatus.Revoked => "Revoked",
                        ConnectionStatus.Connected when c.LastSeen.HasValue && c.LastSeen.Value < cutoff
                            => "Unreachable",
                        _ => c.Status.ToString( ),
                    };

                    return new AgentHealthDto(
                        c.Id,
                        c.ConnectionName,
                        status,
                        null,
                        null,
                        c.LastSeen,
                        now,
                        string.IsNullOrEmpty( c.AgentVersion ) ? null : c.AgentVersion
                    );
                } )];

                return Results.Ok( results );
            } )
        .WithName( "GetAgentHealth" )
        .RequireAuthorization( Policies.CanRead );
    }

    // ── Agent Activity ──

    /// <summary>
    /// Registers the agent activity timeline endpoint.
    /// </summary>
    private static void MapAgentActivity( WebApplication app ) {
        _ = app.MapGet(
            "/api/v1/agents/activity",
            async (
                int? count,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                int effectiveCount = Math.Clamp( count ?? 10, 1, 100 );

                List<RegisteredConnection> connections = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer )
                    .ToListAsync( ct );

                List<AgentActivityDto> events = [];
                foreach (RegisteredConnection connection in connections) {
                    events.Add( new AgentActivityDto(
                        connection.Id,
                        connection.ConnectionName,
                        "Registered",
                        connection.Created,
                        connection.Status.ToString( ) ) );

                    if (connection.LastSeen.HasValue) {
                        events.Add( new AgentActivityDto(
                            connection.Id,
                            connection.ConnectionName,
                            "Last Seen",
                            connection.LastSeen.Value,
                            connection.Status.ToString( ) ) );
                    }

                    if (connection.Status == ConnectionStatus.Revoked) {
                        events.Add( new AgentActivityDto(
                            connection.Id,
                            connection.ConnectionName,
                            "Revoked",
                            connection.LastUpdated,
                            connection.Status.ToString( ) ) );
                    }
                }

                List<AgentActivityDto> timeline = [.. events
                    .OrderByDescending( e => e.OccurredAtUtc )
                    .Take( effectiveCount )];

                return Results.Ok( timeline );
            } )
        .WithName( "GetAgentActivity" )
        .RequireAuthorization( Policies.CanRead );
    }

    // ── Agent Execute ──

    /// <summary>
    /// Registers the command execution endpoint for dispatching commands to a specific agent.
    /// Creates an ephemeral task and a one-time schedule so the agent picks it up
    /// through the normal schedule engine.  Returns 202 Accepted with the task and
    /// schedule IDs for subsequent polling.
    /// </summary>
    private static void MapAgentExecute( WebApplication app ) {
        _ = app.MapPost(
            "/api/v1/agents/{agentId}/execute",
            async (
                Guid agentId,
                ExecuteCommandRequest request,
                RunNowService runNowService,
                ScheduleInvalidationDispatcher invalidationDispatcher,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                TaskActionType actionType = request.ActionType.HasValue
                    ? (TaskActionType) request.ActionType.Value
                    : TaskActionType.ShellCommand;

                // Look up the agent's tags so the ephemeral task routes to this agent
                string[]? agentTags = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.Id == agentId && c.IsServer )
                    .Select( c => c.Tags )
                    .FirstOrDefaultAsync( ct );

                (long taskId, Guid scheduleId) = await runNowService.CreateEphemeralTaskAsync(
                    request.Command, actionType, agentTags, ct );

                await invalidationDispatcher.InvalidateAsync( scheduleId, ct );

                return Results.Accepted( value: new {
                    taskId,
                    scheduleId,
                    message = "Ephemeral task created. Execution will begin on the next agent sync.",
                } );
            } )
        .WithName( "ExecuteCommand" )
        .RequireAuthorization( Policies.CanExecute );
    }

    // ── Agent Tags ──

    /// <summary>
    /// Registers tag management endpoints for agents and tasks.
    /// </summary>
    private static void MapAgentTags( WebApplication app ) {
        _ = app.MapGet(
            "/api/v1/tags",
            async (
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                // Aggregate all unique tags across registered agents and tasks
                List<string[]> agentTags = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer )
                    .Select( c => c.Tags )
                    .ToListAsync( ct );

                List<string[]> taskTags = await dbContext.Tasks
                    .AsNoTracking( )
                    .Select( t => t.TargetTags )
                    .ToListAsync( ct );

                List<string> allTags = [.. agentTags
                    .Concat( taskTags )
                    .SelectMany( t => t )
                    .Where( t => !string.IsNullOrWhiteSpace( t ) )
                    .Select( t => t.Trim( ) )
                    .Distinct( StringComparer.OrdinalIgnoreCase )
                    .OrderBy( t => t, StringComparer.OrdinalIgnoreCase )];

                return Results.Ok( allTags );
            } )
        .WithName( "GetAllTags" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/v1/agents/{id}/tags",
            async (
                Guid id,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );
                return connection is null ? Results.NotFound( ) : Results.Ok( connection.Tags );
            } )
        .WithName( "GetAgentTags" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPut(
            "/api/v1/agents/{id}/tags",
            async (
                Guid id,
                UpdateAgentTagsRequest request,
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );
                if (connection is null) {
                    return Results.NotFound( );
                }

                connection.Tags = request.Tags;
                _ = await dbContext.SaveChangesAsync( ct );
                return Results.Ok( connection.Tags );
            } )
        .WithName( "UpdateAgentTags" )
        .RequireAuthorization( Policies.CanUpdate );
    }

    // ── Agent Connections (read-only metadata for Server UI) ──

    /// <summary>
    /// Registers connection-centric agent listing endpoints.
    /// </summary>
    private static void MapAgentConnections( WebApplication app ) {
        _ = app.MapGet(
            "/api/v1/agents/connections",
            async (
                WerkrDbContext dbContext,
                CancellationToken ct
            ) => {
                List<RegisteredConnection> connections = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer )
                    .OrderBy( c => c.ConnectionName )
                    .ToListAsync( ct );

                List<AgentConnectionDto> dtos = [.. connections.Select( c => new AgentConnectionDto(
                    c.Id, c.ConnectionName, c.RemoteUrl, c.Status.ToString( ),
                    c.LastSeen, c.Created, c.Tags ) )];

                return Results.Ok( dtos );
            } )
        .WithName( "GetAgentConnections" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet( "/api/v1/agents/connections/{id}", async (
            Guid id,
            WerkrDbContext dbContext,
            CancellationToken ct
        ) => {
            RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );

            if (connection is null) {
                return Results.NotFound( );
            }

            AgentConnectionDto dto = new(
                    connection.Id, connection.ConnectionName, connection.RemoteUrl,
                    connection.Status.ToString( ), connection.LastSeen, connection.Created,
                    connection.Tags );

            return Results.Ok( dto );
        } )
        .WithName( "GetAgentConnection" )
        .RequireAuthorization( Policies.CanRead );
    }

    // ── Key Rotation ──

    /// <summary>
    /// Registers the endpoint for triggering on-demand cryptographic key rotation for a single agent.
    /// </summary>
    private static void MapAgentKeyRotation( WebApplication app ) {
        _ = app.MapPost( "/api/v1/agents/{id}/rotate-key", async (
            Guid id,
            ClaimsPrincipal user,
            KeyRotationService keyRotationService,
            IAuditService auditService,
            CancellationToken ct
        ) => {
            bool success = await keyRotationService.RotateSingleAgentAsync( id, ct );

            if (success) {
                // Audit: agent key rotated
                string? userId = user.FindFirst( ClaimTypes.NameIdentifier )?.Value;
                await auditService.LogAsync( new AuditEntry(
                    EventTypeId: AuditEventType.AgentKeyRotated.ToEventId( ),
                    ActorId: userId,
                    ActorType: "User",
                    EntityType: "Agent",
                    EntityId: id.ToString( ),
                    ActionPerformed: "KeyRotated"
                ), ct );
            }

            return success
                ? Results.Ok( new { message = "Key rotation completed successfully." } )
                : Results.UnprocessableEntity( new { message = "Key rotation failed. Agent may be unreachable or not connected." } );
        } )
        .WithName( "RotateAgentKey" )
        .RequireAuthorization( Policies.IsAdmin );
    }

}
