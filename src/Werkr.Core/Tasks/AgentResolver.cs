using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Tasks;

/// <summary>
/// Resolves a target agent for task execution based on tag-based matching.
/// An agent is eligible when any of its <see cref="RegisteredConnection.Tags"/>
/// matches any of the task's <c>TargetTags</c> (case-insensitive).
/// </summary>
/// <param name="dbContext">Database context for querying registered connections.</param>
/// <param name="connectionManager">Singleton gRPC channel cache for live health checks.</param>
/// <param name="logger">Logger instance.</param>
public sealed class AgentResolver(
    WerkrDbContext dbContext,
    AgentConnectionManager connectionManager,
    ILogger<AgentResolver> logger
) {

    /// <summary>
    /// Finds a connected agent whose tags intersect with the specified target tags.
    /// First checks agents with <c>Status == Connected</c>. If none are found,
    /// performs a live gRPC health check against all non-revoked matching agents
    /// to recover agents that came back online between health sweeps.
    /// </summary>
    /// <param name="targetTags">The tags to match against agent tags.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A matching <see cref="RegisteredConnection"/>, or null if no agents match.</returns>
    public async Task<RegisteredConnection?> ResolveAsync(
        string[] targetTags,
        CancellationToken ct = default
    ) {
        if (targetTags.Length == 0) {
            logger.LogWarning( "No target tags specified for agent resolution." );
            return null;
        }

        // Normalize target tags for case-insensitive comparison
        HashSet<string> normalizedTargets = new(
            targetTags.Select( t => t.Trim( ) ),
            StringComparer.OrdinalIgnoreCase
        );

        // Load connected server-side agents with tags
        List<RegisteredConnection> connectedAgents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status == Common.Models.ConnectionStatus.Connected )
            .ToListAsync( ct );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "AgentResolver found {AgentCount} connected agents. Target tags: [{Tags}].",
                connectedAgents.Count,
                string.Join(
                    ", ",
                    targetTags
                ) );
        }

        // Find first connected agent with intersecting tags (in-memory for JSON column compatibility)
        RegisteredConnection? match = connectedAgents.FirstOrDefault( agent =>
            agent.Tags.Any( tag => normalizedTargets.Contains( tag.Trim( ) ) ) );

        // If no connected match, try live health check against non-revoked agents with matching tags
        match ??= await TryLiveResolveAsync(
            normalizedTargets,
            ct
        );

        if (match is null) {
            if (logger.IsEnabled( LogLevel.Warning )) {
                logger.LogWarning( "No connected agent found matching tags: [{Tags}].",
                    string.Join(
                        ", ",
                        targetTags
                    ) );
            }
        } else {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Resolved agent {AgentId} ({AgentName}) for tags [{Tags}].",
                    match.Id.ToString( ),
                    match.ConnectionName,
                    string.Join(
                        ", ",
                        targetTags
                    ) );
            }
        }

        return match;
    }

    /// <summary>
    /// Finds all connected agents whose tags intersect with the specified target tags.
    /// </summary>
    /// <param name="targetTags">The tags to match against agent tags.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching <see cref="RegisteredConnection"/> instances.</returns>
    public async Task<IReadOnlyList<RegisteredConnection>> ResolveAllAsync(
        string[] targetTags,
        CancellationToken ct = default
    ) {
        if (targetTags.Length == 0) {
            return [];
        }

        HashSet<string> normalizedTargets = new(
            targetTags.Select( t => t.Trim( ) ),
            StringComparer.OrdinalIgnoreCase
        );

        List<RegisteredConnection> agents = await dbContext.RegisteredConnections
            .Where( c => c.IsServer && c.Status == Common.Models.ConnectionStatus.Connected )
            .ToListAsync( ct );

        return [.. agents.Where( agent => agent.Tags.Any( tag => normalizedTargets.Contains( tag.Trim( ) ) ) )];
    }

    /// <summary>
    /// Performs a live gRPC health check against non-revoked agents with matching tags
    /// that are NOT currently marked as Connected. If an agent responds, updates its
    /// DB status to Connected and returns it.
    /// </summary>
    private async Task<RegisteredConnection?> TryLiveResolveAsync(
        HashSet<string> normalizedTargets,
        CancellationToken ct
    ) {

        // Get non-revoked, non-connected agents
        List<RegisteredConnection> candidates = await dbContext.RegisteredConnections
            .Where( c => c.IsServer
                && c.Status != Common.Models.ConnectionStatus.Connected
                && c.Status != Common.Models.ConnectionStatus.Revoked )
            .ToListAsync( ct );

        // Filter by tags in-memory
        List<RegisteredConnection> tagMatches = [.. candidates.Where( agent =>
            agent.Tags.Any( tag => normalizedTargets.Contains( tag.Trim( ) ) ) )];

        if (tagMatches.Count == 0) {
            return null;
        }

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "AgentResolver attempting live health check on {Count} non-connected candidate(s).",
                tagMatches.Count
            );
        }

        foreach (RegisteredConnection candidate in tagMatches) {
            try {
                (
                    GrpcChannel channel,
                    RegisteredConnection resolved
                ) =
                    await connectionManager.GetChannelAsync(
                        candidate.Id,
                        ct
                    );

                string keyId = resolved.ActiveKeyId ?? resolved.Id.ToString( );
                HeartbeatRequest heartbeat = new( ) { StatusMessage = "live-resolve" };
                EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                    heartbeat, resolved.SharedKey, keyId );

                ConnectionManagement.ConnectionManagementClient client = new( channel );
                EncryptedEnvelope responseEnvelope = await client.HeartbeatAsync(
                    requestEnvelope,
                    AgentConnectionManager.CreateCallOptions(
                        resolved,
                        timeout: TimeSpan.FromSeconds( 5 ),
                        cancellationToken: ct
                    )
                    );

                // Decrypt to validate shared key
                HeartbeatResponse heartbeatResponse = PayloadEncryptor.DecryptFromEnvelope<HeartbeatResponse>(
                    responseEnvelope, resolved.SharedKey );

                // Agent responded — update DB status and return it
                candidate.Status = Common.Models.ConnectionStatus.Connected;
                candidate.LastSeen = DateTime.UtcNow;
                _ = await dbContext.SaveChangesAsync( ct );

                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation(
                        "AgentResolver recovered agent {AgentId} ({Name}) via live health check.",
                        candidate.Id,
                        candidate.ConnectionName
                    );
                }

                return candidate;
            } catch (OperationCanceledException) {
                throw;
            } catch (RpcException) {
                // Agent still unreachable — skip
            } catch (Exception ex) {
                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug( ex,
                        "AgentResolver live probe failed for {AgentId}.",
                        candidate.Id
                    );
                }
            }
        }

        return null;
    }
}
