using Microsoft.EntityFrameworkCore;
using Serilog;
using Werkr.Common.Auth;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Endpoints;

/// <summary>Maps settings-related REST endpoints.</summary>
internal static class SettingsEndpoints {
    /// <summary>Maps the notify-URL-change endpoint.</summary>
    public static WebApplication MapSettingsEndpoints( this WebApplication app ) {
        _ = app.MapPost( "/api/settings/notify-url-change", async (
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

        return app;
    }
}
