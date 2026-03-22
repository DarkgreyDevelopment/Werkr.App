using Grpc.Core;
using Werkr.Common.Models.Audit;
using Werkr.Common.Protos;
using Werkr.Core.Audit;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for generic audit event submission from agents.
/// Decrypts the encrypted envelope, deserializes entries, and records them via <see cref="IAuditService"/>.
/// </summary>
public sealed partial class AuditEventGrpcService(
    IAuditService auditService,
    SecureResponseBuilder builder,
    ILogger<AuditEventGrpcService> logger
) : AuditEventService.AuditEventServiceBase {

    /// <summary>Maximum number of audit entries per batch to prevent resource exhaustion.</summary>
    private const int MaxBatchSize = 500;

    /// <summary>
    /// Accepts a batch of audit events from an agent, wrapped in an encrypted envelope.
    /// </summary>
    public override async Task<EncryptedEnvelope> SubmitAuditEvents(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        (RegisteredConnection connection, SubmitAuditEventsRequest inner) = SecureResponseBuilder.DecryptRequest<SubmitAuditEventsRequest>( request, context );

        if (inner.Entries.Count > MaxBatchSize) {
            throw new RpcException( new Status( StatusCode.InvalidArgument,
                $"Batch size {inner.Entries.Count} exceeds maximum {MaxBatchSize}." ) );
        }

        int accepted = 0;
        foreach (GenericAuditEntry entry in inner.Entries) {
            try {
                AuditEntry auditEntry = new(
                    EventTypeId: entry.EventTypeId,
                    ActorId: !string.IsNullOrEmpty( entry.ActorId ) ? entry.ActorId : connection.Id.ToString( ),
                    ActorType: !string.IsNullOrEmpty( entry.ActorType ) ? entry.ActorType : "Agent",
                    EntityType: !string.IsNullOrEmpty( entry.EntityType ) ? entry.EntityType : null,
                    EntityId: !string.IsNullOrEmpty( entry.EntityId ) ? entry.EntityId : null,
                    ActionPerformed: entry.Action,
                    Details: !string.IsNullOrEmpty( entry.DetailsJson ) ? entry.DetailsJson : null,
                    CorrelationId: !string.IsNullOrEmpty( entry.CorrelationId ) ? entry.CorrelationId : null
                );

                await auditService.LogAsync( auditEntry, context.CancellationToken );
                accepted++;
            } catch (Exception ex) {
                string sanitizedTypeId = entry.EventTypeId[..Math.Min( entry.EventTypeId.Length, 128 )]
                    .Replace( '\n', ' ' ).Replace( '\r', ' ' );
                LogEntryFailed( logger, sanitizedTypeId, ex );
            }
        }

        return await builder.EncryptResponseAsync(
            new SubmitAuditEventsResponse { AcceptedCount = accepted }, connection, context.CancellationToken );
    }

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Failed to record agent audit entry for event type '{EventTypeId}'" )]
    private static partial void LogEntryFailed( ILogger logger, string eventTypeId, Exception ex );
}
