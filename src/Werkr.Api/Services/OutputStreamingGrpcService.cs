using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Werkr.Common.Protos;

namespace Werkr.Api.Services;

/// <summary>
/// Server-side gRPC handler that receives the persistent bidirectional output stream
/// from agents.  When a browser client subscribes via SSE, this service sends an
/// <see cref="OutputSubscription"/> to the appropriate agent so it starts pushing
/// <see cref="OutputMessage"/> lines, which are then forwarded to SSE consumers.
/// <para>
/// Registered as a singleton.  Each connected agent holds one
/// <see cref="AgentStream"/> that wraps the duplex call.
/// </para>
/// </summary>
/// <param name="logger">Logger.</param>
public sealed partial class OutputStreamingGrpcService(
    ILogger<OutputStreamingGrpcService> logger
) : Werkr.Common.Protos.OutputStreamingService.OutputStreamingServiceBase {

    // ── Agent stream tracking ────────────────────────────────────────────────────

    /// <summary>
    /// Holds per-agent stream state: the response writer for sending subscriptions
    /// back to the agent and a set of SSE consumers keyed by execution.
    /// </summary>
    private sealed class AgentStream {
        public required IServerStreamWriter<OutputSubscription> ResponseWriter { get; init; }
        public ConcurrentDictionary<string, Channel<OutputMessage>> Consumers { get; } = new( );
    }

    /// <summary>All currently connected agent streams keyed by peer address.</summary>
    private readonly ConcurrentDictionary<string, AgentStream> _agents = new( );

    // ── gRPC Entry Point ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called once per agent when it opens the persistent output stream.
    /// Reads incoming <see cref="OutputMessage"/> and fans them out to
    /// any registered SSE consumers.
    /// </summary>
    public override async Task StreamOutput(
        IAsyncStreamReader<OutputMessage> requestStream,
        IServerStreamWriter<OutputSubscription> responseStream,
        ServerCallContext context
    ) {
        string peer = context.Peer ?? Guid.NewGuid( ).ToString( );

        AgentStream agentStream = new( ) { ResponseWriter = responseStream };
        _ = _agents.TryAdd( peer, agentStream );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Agent output stream connected from {Peer}.", peer );
        }

        try {
            await foreach (OutputMessage message in requestStream.ReadAllAsync( context.CancellationToken )) {
                string consumerKey = BuildConsumerKey( message.TaskId, message.ScheduleId );

                // Fan out to any SSE consumers for this execution
                foreach (KeyValuePair<string, Channel<OutputMessage>> kvp in agentStream.Consumers) {
                    if (kvp.Key == consumerKey) {
                        _ = kvp.Value.Writer.TryWrite( message );
                    }
                }
            }
        } catch (OperationCanceledException) {
            // Agent disconnected or server shutting down
        } catch (Exception ex) {
            logger.LogWarning( ex, "Agent output stream from {Peer} ended with error.", peer );
        } finally {
            _ = _agents.TryRemove( peer, out _ );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Agent output stream disconnected from {Peer}.", peer );
            }
        }
    }

    // ── Public API (called by SSE endpoints) ─────────────────────────────────────

    /// <summary>
    /// Subscribes a browser SSE consumer to output for a specific task/schedule
    /// execution.  Sends an <see cref="OutputSubscription"/> to all connected agents
    /// so that matching output is pushed over the stream.
    /// </summary>
    /// <param name="taskId">The task to subscribe to.</param>
    /// <param name="scheduleId">The schedule to subscribe to.</param>
    /// <returns>
    /// A <see cref="Channel{OutputMessage}"/> that the SSE endpoint can read from,
    /// or <c>null</c> if no agent stream is available.
    /// </returns>
    public async Task<Channel<OutputMessage>?> SubscribeAsync(
        long taskId,
        string scheduleId
    ) {
        string consumerKey = BuildConsumerKey( taskId, scheduleId );
        Channel<OutputMessage> channel = Channel.CreateUnbounded<OutputMessage>(
            new UnboundedChannelOptions { SingleReader = true } );

        bool any = false;
        foreach (KeyValuePair<string, AgentStream> kvp in _agents) {
            _ = kvp.Value.Consumers.TryAdd( consumerKey, channel );
            try {
                await kvp.Value.ResponseWriter.WriteAsync( new OutputSubscription {
                    TaskId = taskId,
                    ScheduleId = scheduleId,
                    Subscribe = true,
                } );
                any = true;
            } catch (Exception ex) {
                logger.LogWarning( ex, "Failed to send subscribe to agent {Peer}.", kvp.Key );
            }
        }

        return any ? channel : null;
    }

    /// <summary>
    /// Unsubscribes a browser SSE consumer from output for a specific execution.
    /// </summary>
    /// <param name="taskId">The task to unsubscribe from.</param>
    /// <param name="scheduleId">The schedule to unsubscribe from.</param>
    public async Task UnsubscribeAsync( long taskId, string scheduleId ) {
        string consumerKey = BuildConsumerKey( taskId, scheduleId );

        foreach (KeyValuePair<string, AgentStream> kvp in _agents) {
            _ = kvp.Value.Consumers.TryRemove( consumerKey, out _ );
            try {
                await kvp.Value.ResponseWriter.WriteAsync( new OutputSubscription {
                    TaskId = taskId,
                    ScheduleId = scheduleId,
                    Subscribe = false,
                } );
            } catch (Exception ex) {
                logger.LogWarning( ex, "Failed to send unsubscribe to agent {Peer}.", kvp.Key );
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string BuildConsumerKey( long taskId, string scheduleId )
        => $"{taskId}:{scheduleId}";
}
