using System.Threading.Channels;
using Grpc.Core;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// gRPC service hosted on the Agent that receives schedule invalidation
/// notifications from the Server. When a schedule is updated or deleted,
/// the Server calls this service so the Agent can re-sync immediately
/// rather than waiting for its next periodic sync interval.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="invalidationChannel">
/// Channel used to signal the <see cref="Scheduling.ScheduleEvaluatorService"/> that a
/// schedule needs re-syncing. Writers are this service; the reader is
/// the evaluator's background loop.
/// </param>
/// <param name="logger">Logger instance.</param>
public sealed class ScheduleInvalidationService(
    Channel<string> invalidationChannel,
    ILogger<ScheduleInvalidationService> logger
) : ScheduleInvalidation.ScheduleInvalidationBase {

    /// <summary>
    /// Handles a schedule invalidation push from the Server.
    /// Decrypts the envelope to <see cref="InvalidateScheduleRequest"/>, then
    /// writes the schedule ID to the shared invalidation channel so the
    /// <see cref="Scheduling.ScheduleEvaluatorService"/> can trigger a re-sync.
    /// </summary>
    /// <returns>The encrypted acknowledgement envelope.</returns>
    public override async Task<EncryptedEnvelope> InvalidateSchedule(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        InvalidateScheduleRequest inner = PayloadEncryptor.DecryptFromEnvelope<InvalidateScheduleRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ScheduleId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Schedule ID is required." ) );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Received schedule invalidation for ScheduleId={ScheduleId}.",
                inner.ScheduleId );
        }

        // Write to the unbounded channel — never blocks
        bool written = invalidationChannel.Writer.TryWrite( inner.ScheduleId );
        if (!written) {
            logger.LogWarning(
                "Invalidation channel rejected write for ScheduleId={ScheduleId}. Channel may be completed.",
                inner.ScheduleId );
        }

        InvalidateScheduleResponse response = new( ) {
            Acknowledged = written,
        };

        return await Task.FromResult(
            PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId ) );
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
