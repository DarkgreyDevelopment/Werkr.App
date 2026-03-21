using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Configuration;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Endpoints;

/// <summary>Maps settings-related REST endpoints (configuration + URL change notification).</summary>
internal static class SettingsEndpoints {
    /// <summary>Maps configuration CRUD and URL change notification endpoints.</summary>
    public static WebApplication MapSettingsEndpoints( this WebApplication app ) {

        // ── Existing: notify agents of URL change ──
        _ = app.MapPost( "/api/v1/settings/notify-url-change", async (
            NotifyUrlChangeRequest request,
            WerkrDbContext dbContext,
            AgentConnectionManager connectionManager,
            CancellationToken ct
        ) => {
            if (string.IsNullOrWhiteSpace( request.NewServerUrl )) {
                return Results.BadRequest( new { message = "NewServerUrl is required." } );
            }

            if (!Uri.TryCreate( request.NewServerUrl, UriKind.Absolute, out Uri? parsedUri )
                || (parsedUri.Scheme != "https" && parsedUri.Scheme != "http")) {
                return Results.BadRequest( new { message = "NewServerUrl must be a valid HTTP or HTTPS URL." } );
            }

            List<RegisteredConnection> agents = await dbContext.RegisteredConnections
                    .AsNoTracking( )
                    .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
                    .ToListAsync( ct );

            int notified = 0;
            List<string> failedAgents = [];

            Werkr.Common.Protos.NotifyServerUrlChangedRequest grpcRequest = new( ) {
                NewServerUrl = request.NewServerUrl
            };

            foreach (RegisteredConnection agent in agents) {
                try {
                    (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection conn) =
                        await connectionManager.GetChannelAsync( agent.Id, ct );

                    string keyId = conn.ActiveKeyId ?? conn.Id.ToString( );
                    EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                            grpcRequest, conn.SharedKey, keyId );

                    Grpc.Core.CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
                            conn,
                            timeout: TimeSpan.FromSeconds( 15 ),
                            cancellationToken: ct );

                    ConnectionManagement.ConnectionManagementClient client = new( channel );
                    EncryptedEnvelope responseEnvelope =
                            await client.NotifyServerUrlChangedAsync( envelope, callOptions );

                    NotifyServerUrlChangedResponse response = PayloadEncryptor.DecryptFromEnvelope<NotifyServerUrlChangedResponse>(
                            responseEnvelope, conn.SharedKey );

                    if (response.Acknowledged) {
                        notified++;
                    } else {
                        failedAgents.Add( agent.ConnectionName );
                    }
                } catch (Exception ex) {
                    Log.Warning( ex,
                        "Failed to notify agent {AgentId} ({Name}) of URL change.",
                        agent.Id, agent.ConnectionName );
                    failedAgents.Add( agent.ConnectionName );
                }
            }

            return Results.Ok( new NotifyUrlChangeResponse( notified, failedAgents.Count, failedAgents ) );
        } )
        .WithName( "NotifyUrlChange" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Configuration CRUD ──

        _ = app.MapGet( "/api/v1/settings", async (
            string? category,
            int? scopeLevel,
            string? scopeId,
            IConfigurationResolutionService configService,
            CancellationToken ct
        ) => {
            IReadOnlyList<ConfigurationEntryDto> entries = await configService.GetAllAsync(
                category, scopeLevel, scopeId, ct );
            return Results.Ok( entries );
        } )
        .WithName( "GetSettings" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/v1/settings/{key}", async (
            string key,
            IConfigurationResolutionService configService,
            CancellationToken ct
        ) => {
            ConfigurationEntryDto? entry = await configService.GetByKeyAsync( key, ct );
            return entry is null ? Results.NotFound( ) : Results.Ok( entry );
        } )
        .WithName( "GetSetting" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapPut( "/api/v1/settings/{key}", async (
            string key,
            ConfigurationUpdateRequest request,
            HttpContext httpContext,
            IConfigurationResolutionService configService,
            ConfigurationChangeNotifier notifier,
            CancellationToken ct
        ) => {
            try {
                string userId = httpContext.User.FindFirst( ClaimTypes.NameIdentifier )?.Value ?? "unknown";
                ConfigurationEntryDto updated = await configService.UpdateAsync( key, request, userId, ct );

                // Push notification to agents (fire-and-forget, intentionally not propagating request token)
                _ = Task.Run( async ( ) => {
                    try {
                        await notifier.NotifyAsync( updated.SyncVersion, request.ScopeId, CancellationToken.None );
                    } catch (Exception ex) {
                        Log.Warning( ex, "Failed to push config change notification for key {Key}.", key );
                    }
                }, CancellationToken.None );

                return Results.Ok( updated );
            } catch (KeyNotFoundException) {
                return Results.NotFound( );
            } catch (ArgumentException ex) {
                return Results.BadRequest( new { message = ex.Message } );
            }
        } )
        .WithName( "UpdateSetting" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/v1/settings/{key}/history", async (
            string key,
            int? limit,
            IConfigurationResolutionService configService,
            CancellationToken ct
        ) => {
            IReadOnlyList<ConfigurationChangeLogDto> logs = await configService.GetHistoryAsync(
                key, limit ?? 50, ct );
            return Results.Ok( logs );
        } )
        .WithName( "GetSettingHistory" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/v1/agents/{agentId}/settings", async (
            string agentId,
            IConfigurationResolutionService configService,
            CancellationToken ct
        ) => {
            IReadOnlyList<EffectiveConfigurationDto> effective = await configService.GetEffectiveSettingsAsync(
                agentId, ct );
            return Results.Ok( effective );
        } )
        .WithName( "GetAgentEffectiveSettings" )
        .RequireAuthorization( Policies.IsAdmin );

        // ── Encryption key rotation ──
        _ = app.MapPost( "/api/v1/settings/encryption/rotate", async (
            Core.Encryption.IFieldEncryptionKeyRotationService rotationService,
            CancellationToken ct
        ) => {
            Core.Encryption.RotationStatus status = await rotationService.StartRotationAsync( ct );
            return Results.Ok( status );
        } )
        .WithName( "StartKeyRotation" )
        .RequireAuthorization( Policies.IsAdmin );

        _ = app.MapGet( "/api/v1/settings/encryption/rotate/status", async (
            Core.Encryption.IFieldEncryptionKeyRotationService rotationService,
            CancellationToken ct
        ) => {
            Core.Encryption.RotationStatus status = await rotationService.GetStatusAsync( ct );
            return Results.Ok( status );
        } )
        .WithName( "GetKeyRotationStatus" )
        .RequireAuthorization( Policies.IsAdmin );

        return app;
    }
}
