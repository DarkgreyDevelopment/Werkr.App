using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Api.Services;

/// <summary>
/// Server-side service that pushes workflow-disabled notifications to connected agents.
/// When a workflow is disabled, agents with in-flight runs for that workflow should cancel them.
/// </summary>
public sealed partial class WorkflowDisabledDispatcher(
    AgentConnectionManager connectionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowDisabledDispatcher> logger
) {

    /// <summary>
    /// Notifies all connected agents that a workflow has been disabled.
    /// Failures are logged but do not throw.
    /// </summary>
    public async Task NotifyDisabledAsync( long workflowId, CancellationToken ct = default ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        List<RegisteredConnection> agents = await db.RegisteredConnections
            .AsNoTracking( )
            .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
            .ToListAsync( ct );

        if (agents.Count == 0) {
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Sending workflow-disabled notification for WorkflowId={WorkflowId} to {AgentCount} agents.",
                workflowId, agents.Count );
        }

        NotifyWorkflowDisabledRequest innerRequest = new( ) {
            WorkflowId = workflowId,
        };

        await Parallel.ForEachAsync( agents, ct, async ( agent, innerCt ) => {
            try {
                (GrpcChannel channel, RegisteredConnection connection) =
                    await connectionManager.GetChannelAsync( agent.Id, innerCt );
                CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
                    connection,
                    timeout: TimeSpan.FromSeconds( 15 ),
                    cancellationToken: innerCt );

                string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
                EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                    innerRequest, connection.SharedKey, keyId );

                ScheduleInvalidation.ScheduleInvalidationClient client = new( channel );
                EncryptedEnvelope responseEnvelope = await client.NotifyWorkflowDisabledAsync( envelope, callOptions );

                NotifyWorkflowDisabledResponse response = PayloadEncryptor.DecryptFromEnvelope<NotifyWorkflowDisabledResponse>(
                    responseEnvelope, connection.SharedKey );

                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug(
                        "Workflow-disabled notification sent to agent {AgentId}: acknowledged={Ack}.",
                        agent.Id, response.Acknowledged );
                }
            } catch (Exception ex) {
                logger.LogWarning( ex,
                    "Failed to send workflow-disabled notification to agent {AgentId} for WorkflowId={WorkflowId}.",
                    agent.Id, workflowId );
            }
        } );
    }
}
