using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Services;

/// <summary>
/// Server-side service that pushes schedule invalidation notifications to affected agents.
/// When a schedule is updated or deleted, this service identifies all agents whose tags
/// match the tasks using that schedule and sends them an <see cref="InvalidateScheduleRequest"/>
/// so they re-sync immediately.
/// </summary>
/// <param name="connectionManager">Manages gRPC channels to agents.</param>
/// <param name="scopeFactory">Factory for creating DI scopes to resolve <see cref="WerkrDbContext"/>.</param>
/// <param name="logger">Logger instance.</param>
public sealed class ScheduleInvalidationDispatcher(
    AgentConnectionManager connectionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduleInvalidationDispatcher> logger
) {

    /// <summary>
    /// Notifies all affected agents that a schedule has been modified or deleted.
    /// <para>
    /// Finds tasks referencing the schedule, identifies agents whose tags overlap,
    /// and sends <c>InvalidateSchedule</c> to each. Failures are logged but do not throw.
    /// </para>
    /// </summary>
    /// <param name="scheduleId">The schedule ID that was changed.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InvalidateAsync( Guid scheduleId, CancellationToken ct = default ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Find tasks referencing this schedule to get their TargetTags
        List<WerkrTask> affectedTasks = await db.TaskSchedules
            .AsNoTracking( )
            .Where( ts => ts.ScheduleId == scheduleId )
            .Select( ts => ts.Task! )
            .ToListAsync( ct );

        if (affectedTasks.Count == 0) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "No tasks reference schedule {ScheduleId}. Skipping invalidation.", scheduleId );
            }
            return;
        }

        // Collect all unique target tags from affected tasks
        HashSet<string> allTargetTags = new( StringComparer.OrdinalIgnoreCase );
        foreach (WerkrTask task in affectedTasks) {
            if (task.TargetTags is { Length: > 0 }) {
                foreach (string tag in task.TargetTags) {
                    _ = allTargetTags.Add( tag );
                }
            }
        }

        // Find all connected agents
        List<RegisteredConnection> agents = await db.RegisteredConnections
            .AsNoTracking( )
            .Where( c => c.IsServer && c.Status == ConnectionStatus.Connected )
            .ToListAsync( ct );

        // Filter agents whose tags overlap with the affected tasks' TargetTags
        List<RegisteredConnection> affectedAgents = [];
        foreach (RegisteredConnection agent in agents) {
            string[]? agentTags = agent.Tags;
            if (agentTags is null || agentTags.Length == 0) {
                continue;
            }

            bool hasOverlap = agentTags.Any( t => allTargetTags.Contains( t ) );
            if (hasOverlap) {
                affectedAgents.Add( agent );
            }
        }

        if (affectedAgents.Count == 0) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "No agents match tags for schedule {ScheduleId}. Skipping invalidation.", scheduleId );
            }
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Sending schedule invalidation for {ScheduleId} to {AgentCount} agents.",
                scheduleId, affectedAgents.Count );
        }

        // Send invalidation to each affected agent (fire-and-forget, log failures)
        InvalidateScheduleRequest innerRequest = new( ) {
            ScheduleId = scheduleId.ToString( ),
        };

        await Parallel.ForEachAsync( affectedAgents, ct, async ( agent, innerCt ) => {
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
                EncryptedEnvelope responseEnvelope = await client.InvalidateScheduleAsync( envelope, callOptions );

                InvalidateScheduleResponse response = PayloadEncryptor.DecryptFromEnvelope<InvalidateScheduleResponse>(
                    responseEnvelope, connection.SharedKey );

                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug(
                        "Schedule invalidation sent to agent {AgentId}: acknowledged={Ack}.",
                        agent.Id, response.Acknowledged );
                }
            } catch (Exception ex) {
                logger.LogWarning( ex,
                    "Failed to send schedule invalidation to agent {AgentId} for schedule {ScheduleId}.",
                    agent.Id, scheduleId );
            }
        } );
    }
}
