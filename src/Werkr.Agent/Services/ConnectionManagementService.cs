using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Agent.Communication;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// gRPC service hosted on the Agent that receives connection management
/// commands from the Server. All RPCs use <see cref="EncryptedEnvelope"/>.
/// Supports server URL change notifications only; heartbeat, key rotation,
/// and configuration change notifications are now handled by
/// <see cref="HeartbeatBackgroundService"/> via the notification mailbox.
/// </summary>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="clientFactory">
/// The <see cref="AgentGrpcClientFactory"/> whose cached channel is reset
/// when the server URL changes.
/// </param>
/// <param name="logger">Logger instance.</param>
public sealed partial class ConnectionManagementService(
    IServiceScopeFactory scopeFactory,
    AgentGrpcClientFactory clientFactory,
    ILogger<ConnectionManagementService> logger
) : ConnectionManagement.ConnectionManagementBase {

    /// <summary>
    /// Handles a server URL change notification from the Server.
    /// Decrypts the envelope, updates the stored server URL in the Agent's local database,
    /// and resets the cached gRPC channel so subsequent calls use the new URL.
    /// </summary>
    /// <returns>The encrypted acknowledgement envelope.</returns>
    public override async Task<EncryptedEnvelope> NotifyServerUrlChanged(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        NotifyServerUrlChangedRequest inner = PayloadEncryptor.DecryptFromEnvelope<NotifyServerUrlChangedRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.NewServerUrl )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "New server URL is required." ) );
        }

        if (!Uri.TryCreate( inner.NewServerUrl, UriKind.Absolute, out Uri? parsedUri )
            || (parsedUri.Scheme != "https" && parsedUri.Scheme != "http")) {
            throw new RpcException( new Status( StatusCode.InvalidArgument,
                "New server URL must be a valid HTTP or HTTPS URL." ) );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Received server URL change notification. New URL: {NewUrl}",
                inner.NewServerUrl );
        }

        try {
            using IServiceScope scope = scopeFactory.CreateScope( );
            WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

            // Agent-side connection records have IsServer == false
            RegisteredConnection? localConnection = await dbContext.RegisteredConnections
                .FirstOrDefaultAsync(
                    c => !c.IsServer && c.Status == ConnectionStatus.Connected,
                    context.CancellationToken );

            if (localConnection is null) {
                logger.LogWarning( "No active server connection found to update." );
                NotifyServerUrlChangedResponse failResponse = new( ) {
                    Acknowledged = false,
                    Message = "No active server connection found."
                };
                return PayloadEncryptor.EncryptToEnvelope( failResponse, connection.SharedKey, keyId );
            }

            string oldUrl = localConnection.RemoteUrl;
            localConnection.RemoteUrl = inner.NewServerUrl;
            _ = await dbContext.SaveChangesAsync( context.CancellationToken );

            // Reset the cached gRPC channel so it reconnects to the new URL
            clientFactory.Reset( );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Server URL updated from {OldUrl} to {NewUrl}. gRPC channel reset.",
                    oldUrl, inner.NewServerUrl );
            }

            NotifyServerUrlChangedResponse response = new( ) {
                Acknowledged = true,
                Message = "Server URL updated successfully."
            };
            return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
        } catch (Exception ex) when (ex is not RpcException) {
            logger.LogError( ex, "Failed to process server URL change notification." );
            throw new RpcException( new Status( StatusCode.Internal,
                $"Failed to update server URL: {ex.Message}" ) );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
