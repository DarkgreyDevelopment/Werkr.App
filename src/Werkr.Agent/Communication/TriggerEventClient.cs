using Grpc.Core;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Communication;

/// <summary>
/// Wraps the <see cref="TriggerEventService.TriggerEventServiceClient"/> gRPC client,
/// handling encrypted envelope serialization and connection resolution.
/// Reports file monitor events to the API.
/// </summary>
/// <param name="clientFactory">Factory for creating outbound gRPC clients.</param>
/// <param name="logger">Logger instance.</param>
public sealed class TriggerEventClient(
    AgentGrpcClientFactory clientFactory,
    ILogger<TriggerEventClient> logger
) {

    /// <summary>
    /// Reports a file monitor event to the API and returns the workflow run ID if accepted.
    /// </summary>
    /// <param name="triggerId">The trigger ID.</param>
    /// <param name="filePath">The file path that triggered the event.</param>
    /// <param name="eventType">The event type (created, changed, deleted, renamed).</param>
    /// <param name="directory">The watched directory.</param>
    /// <param name="timestamp">ISO 8601 timestamp of the event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow run ID if accepted, or <see langword="null"/> on failure.</returns>
    public async Task<string?> ReportFileMonitorEventAsync(
        long triggerId,
        string filePath,
        string eventType,
        string directory,
        string timestamp,
        CancellationToken ct
    ) {
        try {
            TriggerEventService.TriggerEventServiceClient client =
                await clientFactory.CreateTriggerEventServiceClientAsync( ct );
            RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

            FileMonitorEventRequest request = new( ) {
                ConnectionId = connection.Id.ToString( ),
                TriggerId = triggerId,
                FilePath = filePath,
                EventType = eventType,
                Directory = directory,
                Timestamp = timestamp,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            EncryptedEnvelope responseEnvelope = await client.ReportFileMonitorEventAsync( envelope, callOptions );

            FileMonitorEventResponse response = clientFactory.DecryptAndCheckUrgency<FileMonitorEventResponse>(
                responseEnvelope );

            if (!response.Accepted) {
                logger.LogWarning(
                    "Server rejected file monitor event for trigger {TriggerId}: {Error}",
                    triggerId, response.Error );
                return null;
            }

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "File monitor event accepted for trigger {TriggerId}. WorkflowRunId={RunId}.",
                    triggerId, response.WorkflowRunId );
            }

            return response.WorkflowRunId;
        } catch (Exception ex) {
            logger.LogWarning( ex,
                "Failed to report file monitor event for trigger {TriggerId}.",
                triggerId );
            return null;
        }
    }
}
