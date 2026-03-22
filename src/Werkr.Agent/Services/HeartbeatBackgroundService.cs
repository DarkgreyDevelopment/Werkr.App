using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Agent.Communication;
using Werkr.Agent.Scheduling;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// Background service that sends periodic heartbeats to the API and processes
/// any pending notifications returned in the response (schedule invalidation,
/// config updates, key rotation, workflow disable, module approval).
/// Uses an urgency channel to allow other components to request an immediate heartbeat.
/// </summary>
/// <param name="clientFactory">Factory for creating outbound gRPC clients to the Server.</param>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="invalidationChannel">Channel for signalling schedule invalidation.</param>
/// <param name="configChangeChannel">Channel for signalling configuration change notifications.</param>
/// <param name="urgencyChannel">Channel that triggers an immediate heartbeat when written to.</param>
/// <param name="workflowExecutionService">Service for cancelling in-flight workflow runs.</param>
/// <param name="logger">Logger instance.</param>
/// <param name="interval">Heartbeat interval. Defaults to 60 seconds.</param>
public sealed partial class HeartbeatBackgroundService(
    AgentGrpcClientFactory clientFactory,
    IServiceScopeFactory scopeFactory,
    Channel<string> invalidationChannel,
    Channel<long> configChangeChannel,
    Channel<bool> urgencyChannel,
    WorkflowExecutionService workflowExecutionService,
    ILogger<HeartbeatBackgroundService> logger,
    TimeSpan? interval = null
) : BackgroundService {

    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds( 60 );

    private static readonly DateTime s_processStartUtc = Process.GetCurrentProcess( ).StartTime.ToUniversalTime( );

    // Backoff steps for consecutive failures: 5s, 10s, 30s, 60s cap.
    private static readonly TimeSpan[] s_backoffSteps = [
        TimeSpan.FromSeconds( 5 ),
        TimeSpan.FromSeconds( 10 ),
        TimeSpan.FromSeconds( 30 ),
        TimeSpan.FromSeconds( 60 ),
    ];

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogStarting( logger );

        // Wait for the agent to complete registration before sending heartbeats
        await WaitForRegistrationAsync( stoppingToken );

        int consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await SendHeartbeatAsync( stoppingToken );
                consecutiveFailures = 0;

                // Urgency-aware delay: wait for interval OR urgent signal, whichever comes first
                await WaitForNextHeartbeatAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (RpcException ex) {
                consecutiveFailures++;
                LogHeartbeatRpcFailed( logger, ex.StatusCode, ex.Message, ex );
                await BackoffAsync( consecutiveFailures, stoppingToken );
            } catch (Exception ex) {
                consecutiveFailures++;
                LogHeartbeatFailed( logger, ex );
                await BackoffAsync( consecutiveFailures, stoppingToken );
            }
        }

        LogStopping( logger );
    }

    // ── Registration Wait ────────────────────────────────────────────────────────

    /// <summary>
    /// Polls <see cref="AgentGrpcClientFactory.IsRegisteredAsync"/> every 5 seconds
    /// until the agent has completed registration.
    /// </summary>
    private async Task WaitForRegistrationAsync( CancellationToken ct ) {
        while (!ct.IsCancellationRequested) {
            if (await clientFactory.IsRegisteredAsync( ct )) {
                LogRegistered( logger );
                return;
            }

            LogWaitingForRegistration( logger );
            await Task.Delay( TimeSpan.FromSeconds( 5 ), ct );
        }
    }

    // ── Heartbeat ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds, encrypts, and sends a heartbeat to the API, then processes any
    /// pending notifications in the response.
    /// </summary>
    private async Task SendHeartbeatAsync( CancellationToken ct ) {
        RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

        long uptimeSeconds = (long)( DateTime.UtcNow - s_processStartUtc ).TotalSeconds;

        AgentHeartbeatRequest request = new( ) {
            ConnectionId = connection.Id.ToString( ),
            AgentVersion = VersionHelper.GetAgentVersion( ),
            UptimeSeconds = uptimeSeconds,
            ActiveScheduleCount = 0,
            StatusMessage = "ok",
        };

        byte[] sharedKey = clientFactory.GetSharedKey( );
        string keyId = clientFactory.GetKeyId( );
        EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( request, sharedKey, keyId );

        AgentHeartbeat.AgentHeartbeatClient client = await clientFactory.CreateAgentHeartbeatClientAsync( ct );
        CallOptions callOptions = clientFactory.CreateCallOptions(
            timeout: TimeSpan.FromSeconds( 30 ), cancellationToken: ct );
        EncryptedEnvelope responseEnvelope = await client.HeartbeatAsync( envelope, callOptions );

        AgentHeartbeatResponse response = PayloadEncryptor.DecryptFromEnvelope<AgentHeartbeatResponse>(
            responseEnvelope, sharedKey );

        // Process pending notifications
        foreach (PendingNotification notification in response.PendingNotifications) {
            await ProcessNotificationAsync( notification, ct );
        }

        LogHeartbeatSuccess( logger, response.ServerVersion );
    }

    // ── Notification Processing ──────────────────────────────────────────────────

    /// <summary>
    /// Routes a pending notification to the appropriate handler based on its channel name.
    /// </summary>
    private async Task ProcessNotificationAsync( PendingNotification notification, CancellationToken ct ) {
        switch (notification.Channel) {
            case "schedule_invalidation":
                _ = invalidationChannel.Writer.TryWrite( notification.Payload );
                LogNotificationProcessed( logger, notification.Channel );
                break;

            case "config_update":
                if (long.TryParse( notification.Payload, out long version )) {
                    _ = configChangeChannel.Writer.TryWrite( version );
                }
                LogNotificationProcessed( logger, notification.Channel );
                break;

            case "key_rotation":
                LogNotificationProcessed( logger, notification.Channel );
                await HandleKeyRotationAsync( ct );
                break;

            case "workflow_disabled":
                if (long.TryParse( notification.Payload, out long workflowId )) {
                    workflowExecutionService.CancelWorkflow( workflowId );
                }
                LogNotificationProcessed( logger, notification.Channel );
                break;

            case "module_approval":
                LogModuleApprovalReceived( logger, notification.Payload );
                break;

            default:
                LogUnknownNotification( logger, notification.Channel );
                break;
        }
    }

    // ── Key Rotation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles agent-initiated key rotation: fetches the pending key from the API,
    /// decrypts it with the Agent's RSA private key, persists the key swap locally,
    /// resets the client factory, and acknowledges the new key to the API.
    /// </summary>
    private async Task HandleKeyRotationAsync( CancellationToken ct ) {
        KeyExchange.KeyExchangeClient client = await clientFactory.CreateKeyExchangeClientAsync( ct );
        RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

        // Fetch pending key
        FetchPendingKeyRequest fetchRequest = new( ) {
            ConnectionId = connection.Id.ToString( ),
        };

        byte[] sharedKey = clientFactory.GetSharedKey( );
        string keyId = clientFactory.GetKeyId( );
        EncryptedEnvelope fetchEnvelope = PayloadEncryptor.EncryptToEnvelope( fetchRequest, sharedKey, keyId );

        CallOptions callOptions = clientFactory.CreateCallOptions(
            timeout: TimeSpan.FromSeconds( 30 ), cancellationToken: ct );
        EncryptedEnvelope fetchResponseEnvelope = await client.FetchPendingKeyAsync( fetchEnvelope, callOptions );

        FetchPendingKeyResponse fetchResponse = PayloadEncryptor.DecryptFromEnvelope<FetchPendingKeyResponse>(
            fetchResponseEnvelope, sharedKey );

        if (!fetchResponse.HasPendingKey) {
            LogNoPendingKey( logger );
            return;
        }

        // RSA-decrypt the new AES-256 key
        using RSA rsa = RSA.Create( );
        rsa.ImportParameters( connection.LocalPrivateKey );
        byte[] newKey = rsa.Decrypt( fetchResponse.RsaEncryptedNewKey.ToByteArray( ), RSAEncryptionPadding.OaepSHA512 );

        // Persist locally: swap current -> previous, install new
        using (IServiceScope scope = scopeFactory.CreateScope( )) {
            WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

            RegisteredConnection localConnection = await dbContext.RegisteredConnections
                .FirstAsync( c => !c.IsServer && c.Status == ConnectionStatus.Connected, ct );

            localConnection.PreviousSharedKey = localConnection.SharedKey;
            localConnection.PreviousKeyId = localConnection.ActiveKeyId;
            localConnection.SharedKey = newKey;
            localConnection.ActiveKeyId = fetchResponse.NewKeyId;

            _ = await dbContext.SaveChangesAsync( ct );
        }

        // Reset the client factory so it picks up the new connection
        clientFactory.Reset( );

        LogKeyRotated( logger, fetchResponse.NewKeyId );

        // Acknowledge using the NEW key so the server knows the agent transitioned
        AcknowledgeKeyRequest ackRequest = new( ) {
            ConnectionId = connection.Id.ToString( ),
            ActivatedKeyId = fetchResponse.NewKeyId,
        };

        EncryptedEnvelope ackEnvelope = PayloadEncryptor.EncryptToEnvelope( ackRequest, newKey, fetchResponse.NewKeyId );

        // Re-create client and call options after Reset (channel was disposed)
        KeyExchange.KeyExchangeClient ackClient = await clientFactory.CreateKeyExchangeClientAsync( ct );
        CallOptions ackCallOptions = clientFactory.CreateCallOptions(
            timeout: TimeSpan.FromSeconds( 30 ), cancellationToken: ct );
        _ = await ackClient.AcknowledgeKeyAsync( ackEnvelope, ackCallOptions );

        LogKeyAcknowledged( logger, fetchResponse.NewKeyId );
    }

    // ── Urgency-Aware Delay ──────────────────────────────────────────────────────

    /// <summary>
    /// Waits for the configured interval, but returns early if the urgency channel
    /// is signalled (e.g., when another component detects urgent commands pending).
    /// </summary>
    private async Task WaitForNextHeartbeatAsync( CancellationToken stoppingToken ) {
        using CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource( stoppingToken );
        Task delayTask = Task.Delay( _interval, delayCts.Token );
        Task urgencyTask = urgencyChannel.Reader.WaitToReadAsync( stoppingToken ).AsTask( );

        Task completed = await Task.WhenAny( delayTask, urgencyTask );
        if (completed == urgencyTask) {
            // Drain all urgency signals
            while (urgencyChannel.Reader.TryRead( out _ )) { }
            await delayCts.CancelAsync( );
        }
    }

    // ── Backoff ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Delays with exponential backoff based on consecutive failure count.
    /// Steps: 5s, 10s, 30s, 60s (capped).
    /// </summary>
    private static async Task BackoffAsync( int consecutiveFailures, CancellationToken ct ) {
        int index = Math.Min( consecutiveFailures - 1, s_backoffSteps.Length - 1 );
        await Task.Delay( s_backoffSteps[index], ct );
    }

    // ── LoggerMessage Definitions ────────────────────────────────────────────────

    [LoggerMessage( Level = LogLevel.Information, Message = "HeartbeatBackgroundService starting." )]
    private static partial void LogStarting( ILogger logger );

    [LoggerMessage( Level = LogLevel.Information, Message = "HeartbeatBackgroundService stopping." )]
    private static partial void LogStopping( ILogger logger );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Waiting for agent registration before starting heartbeats." )]
    private static partial void LogWaitingForRegistration( ILogger logger );

    [LoggerMessage( Level = LogLevel.Information, Message = "Agent is registered. Starting heartbeat loop." )]
    private static partial void LogRegistered( ILogger logger );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Heartbeat sent successfully. Server version: {ServerVersion}." )]
    private static partial void LogHeartbeatSuccess( ILogger logger, string serverVersion );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Heartbeat RPC failed. Status={StatusCode}, Detail={Detail}." )]
    private static partial void LogHeartbeatRpcFailed( ILogger logger, StatusCode statusCode, string detail, Exception ex );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Heartbeat failed." )]
    private static partial void LogHeartbeatFailed( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Processed notification: {Channel}." )]
    private static partial void LogNotificationProcessed( ILogger logger, string channel );

    [LoggerMessage( Level = LogLevel.Information, Message = "Module approval notification received: {Payload}. No action taken." )]
    private static partial void LogModuleApprovalReceived( ILogger logger, string payload );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Unknown notification channel: {Channel}." )]
    private static partial void LogUnknownNotification( ILogger logger, string channel );

    [LoggerMessage( Level = LogLevel.Debug, Message = "No pending key rotation found." )]
    private static partial void LogNoPendingKey( ILogger logger );

    [LoggerMessage( Level = LogLevel.Information, Message = "Key rotation complete. New KeyId={NewKeyId}." )]
    private static partial void LogKeyRotated( ILogger logger, string newKeyId );

    [LoggerMessage( Level = LogLevel.Information, Message = "Key rotation acknowledged. ActiveKeyId={ActiveKeyId}." )]
    private static partial void LogKeyAcknowledged( ILogger logger, string activeKeyId );
}
