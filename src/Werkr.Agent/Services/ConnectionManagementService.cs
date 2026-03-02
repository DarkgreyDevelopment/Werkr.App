using System.Security.Cryptography;

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
/// Supports server URL change notifications, heartbeat, and key rotation.
/// </summary>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="clientFactory">
/// The <see cref="AgentGrpcClientFactory"/> whose cached channel is reset
/// when the server URL changes.
/// </param>
/// <param name="logger">Logger instance.</param>
public sealed class ConnectionManagementService(
    IServiceScopeFactory scopeFactory,
    AgentGrpcClientFactory clientFactory,
    ILogger<ConnectionManagementService> logger
) : ConnectionManagement.ConnectionManagementBase {

    /// <summary>
    /// Handles a server URL change notification from the Server.
    /// Decrypts the envelope, updates the stored server URL in the Agent's local database,
    /// and resets the cached gRPC channel so subsequent calls use the new URL.
    /// </summary>
    public override async Task<EncryptedEnvelope> NotifyServerUrlChanged(
        EncryptedEnvelope request,
        ServerCallContext context ) {

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

    /// <summary>
    /// Application-level heartbeat. Returns agent version and runtime information.
    /// Replaces Grpc.Health.V1 for encrypted health probing.
    /// </summary>
    public override Task<EncryptedEnvelope> Heartbeat(
        EncryptedEnvelope request,
        ServerCallContext context ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        // Decrypt the request (validates the envelope is authentic)
        HeartbeatRequest inner = PayloadEncryptor.DecryptFromEnvelope<HeartbeatRequest>(
            request, connection.SharedKey );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "Heartbeat from server. Agent version reported: {Version}, uptime: {Uptime}s.",
                inner.AgentVersion, inner.UptimeSeconds );
        }

        HeartbeatResponse response = new( ) {
            Acknowledged = true,
            ServerVersion = typeof( ConnectionManagementService ).Assembly
                .GetName( ).Version?.ToString( ) ?? "unknown",
        };

        return Task.FromResult(
            PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId ) );
    }

    /// <summary>
    /// Handles key rotation initiated by the Server. The request contains
    /// a new AES-256 key RSA-encrypted with the Agent's public key.
    /// The envelope itself is encrypted with the current SharedKey.
    /// </summary>
    public override async Task<EncryptedEnvelope> RotateSharedKey(
        EncryptedEnvelope request,
        ServerCallContext context ) {

        RegisteredConnection connection = GetConnection( context );
        string currentKeyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        RotateSharedKeyRequest inner = PayloadEncryptor.DecryptFromEnvelope<RotateSharedKeyRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.NewKeyId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "New key ID is required." ) );
        }

        if (inner.RsaEncryptedNewKey.IsEmpty) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "RSA-encrypted new key is required." ) );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Received key rotation request. New KeyId={NewKeyId}.",
                inner.NewKeyId );
        }

        try {
            // Decrypt the new key using our RSA private key
            using RSA rsa = RSA.Create( );
            rsa.ImportParameters( connection.LocalPrivateKey );
            byte[] newKey = rsa.Decrypt( inner.RsaEncryptedNewKey.ToByteArray( ), RSAEncryptionPadding.OaepSHA256 );

            if (newKey.Length != 32) {
                throw new RpcException( new Status( StatusCode.InvalidArgument,
                    "Decrypted key is not 32 bytes (AES-256)." ) );
            }

            // Persist: move current key to previous, install new key
            using IServiceScope scope = scopeFactory.CreateScope( );
            WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

            RegisteredConnection? localConnection = await dbContext.RegisteredConnections
                .FirstOrDefaultAsync(
                    c => !c.IsServer && c.Status == ConnectionStatus.Connected,
                    context.CancellationToken )
                ?? throw new RpcException( new Status( StatusCode.Internal,
                    "No active connection found for key rotation." ) );

            localConnection.PreviousSharedKey = localConnection.SharedKey;
            localConnection.PreviousKeyId = localConnection.ActiveKeyId;
            localConnection.SharedKey = newKey;
            localConnection.ActiveKeyId = inner.NewKeyId;

            _ = await dbContext.SaveChangesAsync( context.CancellationToken );

            // Reset the client factory so it picks up the new connection
            clientFactory.Reset( );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Key rotation complete. ActiveKeyId={ActiveKeyId}, PreviousKeyId={PreviousKeyId}.",
                    inner.NewKeyId, currentKeyId );
            }

            // Respond with the NEW key so the server knows the agent transitioned
            RotateSharedKeyResponse response = new( ) {
                Success = true,
                ActiveKeyId = inner.NewKeyId,
            };
            return PayloadEncryptor.EncryptToEnvelope( response, newKey, inner.NewKeyId );
        } catch (Exception ex) when (ex is not RpcException) {
            logger.LogError( ex, "Key rotation failed." );
            throw new RpcException( new Status( StatusCode.Internal,
                $"Key rotation failed: {ex.Message}" ) );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
