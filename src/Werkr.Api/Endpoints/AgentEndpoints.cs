using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Cryptography;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

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
            "/api/agents",
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
                    c.LastSeen, c.Created ) )];

                return Results.Ok( agents );
            } )
        .WithName( "GetAgents" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapGet(
            "/api/agents/{id}",
            async (
                Guid id,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
                CancellationToken ct
            ) => {
                RegisteredConnection? connection = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .FirstOrDefaultAsync( c => c.Id == id && c.IsServer, ct );

                if (connection is null) {
                    return Results.NotFound( );
                }

                bool? powerShellAvailable = null;
                bool? systemShellAvailable = null;

                if (connection.Status == ConnectionStatus.Connected) {
                    try {
                        (GrpcChannel channel, RegisteredConnection resolvedConnection)
                            = await connectionManager.GetChannelAsync( id, ct );

                        string keyId = resolvedConnection.ActiveKeyId ?? resolvedConnection.Id.ToString( );
                        HeartbeatRequest heartbeat = new( ) { StatusMessage = "detail-probe" };
                        EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                            heartbeat, resolvedConnection.SharedKey, keyId );

                        ConnectionManagement.ConnectionManagementClient client = new( channel );
                        EncryptedEnvelope responseEnvelope = await client.HeartbeatAsync(
                            requestEnvelope,
                            AgentConnectionManager.CreateCallOptions(
                                resolvedConnection,
                                timeout: TimeSpan.FromSeconds( 5 ),
                                cancellationToken: ct)
                            );

                        // Agent is reachable and shared key is valid
                        HeartbeatResponse _ = PayloadEncryptor.DecryptFromEnvelope<HeartbeatResponse>(
                            responseEnvelope, resolvedConnection.SharedKey );

                        // Shell availability is no longer reported via health checks;
                        // the Heartbeat confirms the agent is alive and encryption works.
                        powerShellAvailable = true;
                        systemShellAvailable = true;
                    } catch (RpcException) {
                        powerShellAvailable = null;
                        systemShellAvailable = null;
                    }
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
                    powerShellAvailable,
                    systemShellAvailable
                );

                return Results.Ok( dto );
            } )
        .WithName( "GetAgentDetail" )
        .RequireAuthorization( Policies.CanRead );

        _ = app.MapPut(
            "/api/agents/{id}",
            async (
                Guid id,
                UpdateAgentRequest request,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
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
                return Results.NoContent( );
            } )
        .WithName( "UpdateAgent" )
        .RequireAuthorization( Policies.CanUpdate );

        _ = app.MapPost(
            "/api/agents/{id}/revoke",
            async (
                Guid id,
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
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
                return Results.Ok( new { message = $"Connection '{connection.ConnectionName}' revoked." } );
            } )
        .WithName( "RevokeAgent" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut(
            "/api/agents/{id}/status",
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
    /// Registers the aggregate agent health endpoint that probes all registered agents via gRPC heartbeat.
    /// </summary>
    private static void MapAgentHealth( WebApplication app ) {
        _ = app.MapGet(
            "/api/agents/health",
            async (
                WerkrDbContext dbContext,
                AgentConnectionManager connectionManager,
                CancellationToken ct
            ) => {
                List<RegisteredConnection> connections = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer )
                    .OrderBy( c => c.ConnectionName )
                    .ToListAsync( ct );

                using CancellationTokenSource timeoutSource = new( TimeSpan.FromSeconds( 10 ) );
                using CancellationTokenSource linkedSource = CancellationTokenSource
                    .CreateLinkedTokenSource( ct, timeoutSource.Token );

                List<Task<AgentHealthDto>> tasks = [.. connections.Select(
                    connection => BuildHealthAsync( connection, connectionManager, linkedSource.Token ) )];

                try {
                    AgentHealthDto[] results = await Task.WhenAll( tasks );
                    return Results.Ok( results.ToList( ) );
                } catch (OperationCanceledException) {
                    List<AgentHealthDto> partial = [.. tasks
                        .Where( task => task.IsCompletedSuccessfully )
                        .Select( task => task.Result )];
                    return Results.Ok( partial );
                }
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
            "/api/agents/activity",
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
    /// </summary>
    private static void MapAgentExecute( WebApplication app ) {
        _ = app.MapPost(
            "/api/agents/{agentId}/execute",
            async (
                Guid agentId,
                ExecuteCommandRequest request,
                CommandDispatcher commandDispatcher,
                CancellationToken ct
            ) => {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( ct );
                if (request.TimeoutMinutes > 0) {
                    cts.CancelAfter( TimeSpan.FromMinutes( request.TimeoutMinutes ) );
                }

                OperatorType operatorType = Enum.Parse<OperatorType>( request.OperatorType, ignoreCase: true );
                List<OperatorOutputLine> results = [];

                await foreach (OperatorOutput output in commandDispatcher.ExecuteCommandAsync(
                    agentId, operatorType, request.Command, cts.Token )) {
                    results.Add( new OperatorOutputLine( output.LogLevel, output.Message, output.Timestamp ) );
                }

                return Results.Ok( new ExecuteCommandResponse( true, results ) );
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
            "/api/tags",
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
            "/api/agents/{id}/tags",
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
            "/api/agents/{id}/tags",
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
            "/api/agents/connections",
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

        _ = app.MapGet( "/api/agents/connections/{id}", async (
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
        _ = app.MapPost( "/api/agents/{id}/rotate-key", async (
            Guid id,
            KeyRotationService keyRotationService,
            CancellationToken ct
        ) => {
            bool success = await keyRotationService.RotateSingleAgentAsync( id, ct );
            return success
                ? Results.Ok( new { message = "Key rotation completed successfully." } )
                : Results.UnprocessableEntity( new { message = "Key rotation failed. Agent may be unreachable or not connected." } );
        } )
        .WithName( "RotateAgentKey" )
        .RequireAuthorization( Policies.IsAdmin );
    }

    // ── Helper ──

    /// <summary>
    /// Builds a health DTO for a single agent by sending a gRPC heartbeat probe and evaluating the response.
    /// </summary>
    private static async Task<AgentHealthDto> BuildHealthAsync(
        RegisteredConnection connection,
        AgentConnectionManager connectionManager,
        CancellationToken cancellationToken
    ) {
        // Skip Revoked agents entirely — they should never reconnect without explicit admin action.
        if (connection.Status == ConnectionStatus.Revoked) {
            return new AgentHealthDto(
                connection.Id,
                connection.ConnectionName,
                connection.Status.ToString( ),
                null,
                null,
                connection.LastSeen,
                DateTime.UtcNow
            );
        }

        try {
            (GrpcChannel channel, RegisteredConnection resolvedConnection)
                = await connectionManager.GetChannelAsync( connection.Id, cancellationToken );

            string keyId = resolvedConnection.ActiveKeyId ?? resolvedConnection.Id.ToString( );
            HeartbeatRequest heartbeat = new( ) { StatusMessage = "health-probe" };
            EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                heartbeat, resolvedConnection.SharedKey, keyId );

            ConnectionManagement.ConnectionManagementClient client = new( channel );
            EncryptedEnvelope responseEnvelope = await client.HeartbeatAsync(
                requestEnvelope,
                AgentConnectionManager.CreateCallOptions(
                    resolvedConnection,
                    timeout: TimeSpan.FromSeconds( 5 ),
                    cancellationToken: cancellationToken ) );

            HeartbeatResponse _ = PayloadEncryptor.DecryptFromEnvelope<HeartbeatResponse>(
                responseEnvelope, resolvedConnection.SharedKey );

            return new AgentHealthDto(
                connection.Id,
                connection.ConnectionName,
                "Connected",
                true,
                true,
                connection.LastSeen,
                DateTime.UtcNow );
        } catch (OperationCanceledException) {
            throw;
        } catch (RpcException) {
            return new AgentHealthDto(
                connection.Id,
                connection.ConnectionName,
                "Unreachable",
                null,
                null,
                connection.LastSeen,
                DateTime.UtcNow );
        } catch (Exception) {
            return new AgentHealthDto(
                connection.Id,
                connection.ConnectionName,
                "Unreachable",
                null,
                null,
                connection.LastSeen,
                DateTime.UtcNow );
        }
    }
}
