using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Werkr.Agent.Communication;
using Werkr.Common.Protos;

namespace Werkr.Agent.Services;

/// <summary>
/// Manages a persistent bidirectional gRPC stream to the Server for real-time
/// output delivery.  The agent opens the stream on startup and pushes
/// <see cref="OutputMessage"/> lines as tasks execute.  The server sends
/// <see cref="OutputSubscription"/> requests back to control which tasks the
/// agent publishes output for.
/// <para>
/// When no subscriptions are active, output is still written to disk and the
/// local database by <see cref="Scheduling.ScheduleEvaluatorService"/>; it is
/// simply not pushed over the stream.
/// </para>
/// <para>
/// A bounded ring buffer of the last <see cref="BufferCapacity"/> lines per
/// executing task is maintained.  Late-arriving subscriptions receive the
/// buffered lines before switching to live output.
/// </para>
/// </summary>
/// <param name="clientFactory">Factory for creating outbound gRPC clients.</param>
/// <param name="logger">Logger.</param>
public sealed partial class OutputStreamingService(
    AgentGrpcClientFactory clientFactory,
    ILogger<OutputStreamingService> logger
) : IDisposable {

    /// <summary>Maximum number of output lines buffered per executing task.</summary>
    internal const int BufferCapacity = 100;

    // ── Subscription tracking ────────────────────────────────────────────────────

    /// <summary>Key for identifying a specific task execution.</summary>
    private readonly record struct ExecutionKey( long TaskId, string ScheduleId );

    /// <summary>Active subscriptions requested by the server.</summary>
    private readonly ConcurrentDictionary<ExecutionKey, bool> _subscriptions = new( );

    /// <summary>Ring buffers holding the last N output lines per execution.</summary>
    private readonly ConcurrentDictionary<ExecutionKey, BoundedBuffer> _buffers = new( );

    /// <summary>Channel used to send messages over the gRPC stream.</summary>
    private readonly Channel<OutputMessage> _outbound = Channel.CreateUnbounded<OutputMessage>(
        new UnboundedChannelOptions { SingleReader = true } );

    private CancellationTokenSource? _streamCts;

    // ── Public API (called by ScheduleEvaluatorService) ──────────────────────────

    /// <summary>
    /// Publishes an output message.  The line is always buffered.  If a
    /// matching subscription is active the message is also enqueued for the
    /// gRPC stream.
    /// </summary>
    public void Publish( OutputMessage message ) {
        ExecutionKey key = new( message.TaskId, message.ScheduleId );

        // Always buffer
        BoundedBuffer buffer = _buffers.GetOrAdd( key, _ => new BoundedBuffer( BufferCapacity ) );
        buffer.Add( message );

        // Only push to stream if subscribed
        if (_subscriptions.ContainsKey( key )) {
            _ = _outbound.Writer.TryWrite( message );
        }
    }

    /// <summary>
    /// Removes the ring buffer for a completed execution, freeing memory.
    /// </summary>
    public void ClearBuffer( long taskId, string scheduleId ) {
        ExecutionKey key = new( taskId, scheduleId );
        _ = _buffers.TryRemove( key, out _ );
    }

    // ── Stream Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the bidirectional stream to the server and begins reading
    /// subscription requests.  Intended to be called once after registration
    /// completes.  Reconnection with exponential backoff is handled internally.
    /// </summary>
    public void Start( CancellationToken appShutdown ) {
        _streamCts = CancellationTokenSource.CreateLinkedTokenSource( appShutdown );
        _ = Task.Run( ( ) => MaintainStreamAsync( _streamCts.Token ), _streamCts.Token );
    }

    /// <inheritdoc/>
    public void Dispose( ) {
        _streamCts?.Cancel( );
        _streamCts?.Dispose( );
    }

    // ── Internal loop ────────────────────────────────────────────────────────────

    private async Task MaintainStreamAsync( CancellationToken ct ) {
        TimeSpan delay = TimeSpan.FromSeconds( 2 );
        TimeSpan maxDelay = TimeSpan.FromSeconds( 60 );

        while (!ct.IsCancellationRequested) {
            try {
                Common.Protos.OutputStreamingService.OutputStreamingServiceClient client =
                    await clientFactory.CreateOutputStreamingClientAsync( ct );
                CallOptions callOptions = clientFactory.CreateCallOptions(
                    timeout: Timeout.InfiniteTimeSpan, cancellationToken: ct );

                using AsyncDuplexStreamingCall<OutputMessage, OutputSubscription> call =
                    client.StreamOutput( callOptions );

                // Use a per-stream token so we can cancel the writer when the
                // reader detects a disconnect (and vice-versa).
                using CancellationTokenSource streamCts =
                    CancellationTokenSource.CreateLinkedTokenSource( ct );

                Task readTask = ReadSubscriptionsAsync( call.ResponseStream, streamCts.Token );
                Task writeTask = WriteOutputAsync( call.RequestStream, streamCts.Token );

                // Give the reader a moment to fail on immediate connection errors
                // (e.g. "Connection refused") before declaring success.
                Task settled = await Task.WhenAny( readTask, Task.Delay( 1000, streamCts.Token ) );
                if (settled == readTask && readTask.IsFaulted) {
                    await readTask; // propagate the connection error
                }

                // Connection confirmed — reset backoff.
                delay = TimeSpan.FromSeconds( 2 );

                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation( "Output streaming connected to server." );
                }

                // Wait for either side to complete or fail.  Without WhenAny a
                // reader failure goes undetected while the writer blocks on an
                // empty outbound channel, leaving the stream in a zombie state.
                Task completed = await Task.WhenAny( readTask, writeTask );
                await streamCts.CancelAsync( );

                // Drain the peer task so it doesn't leak as unobserved.
                Task peer = completed == readTask ? writeTask : readTask;
                try { await peer; } catch (Exception peerEx) {
                    logger.LogDebug( peerEx, "Peer stream task ended during reconnection." );
                }

                // Re-throw the original failure for the reconnection catch block.
                // If the read side completed normally (server closed the stream),
                // this falls through and the outer loop reconnects.
                await completed;
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            } catch (RpcException ex) when (ct.IsCancellationRequested) {
                logger.LogDebug( ex, "Output stream RPC cancelled during shutdown." );
                break;
            } catch (Exception ex) {
                logger.LogWarning( ex, "Output stream disconnected. Reconnecting in {Delay}s.", delay.TotalSeconds );
                _subscriptions.Clear( );

                // Force the gRPC channel to be recreated on the next attempt so
                // stale URLs (from old ports or transient failures) are discarded.
                clientFactory.Reset( );

                try {
                    await Task.Delay( delay, ct );
                } catch (OperationCanceledException) {
                    break;
                }
                delay = TimeSpan.FromTicks( Math.Min( delay.Ticks * 2, maxDelay.Ticks ) );
            }
        }
    }

    private async Task ReadSubscriptionsAsync(
        IAsyncStreamReader<OutputSubscription> reader,
        CancellationToken ct
    ) {
        await foreach (OutputSubscription sub in reader.ReadAllAsync( ct )) {
            ExecutionKey key = new( sub.TaskId, sub.ScheduleId );

            if (sub.Subscribe) {
                _ = _subscriptions.TryAdd( key, true );

                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug( "Subscribed to output for task {TaskId} schedule {ScheduleId}.",
                        sub.TaskId, sub.ScheduleId );
                }

                // Replay buffered lines
                if (_buffers.TryGetValue( key, out BoundedBuffer? buffer )) {
                    foreach (OutputMessage buffered in buffer.Snapshot( )) {
                        _ = _outbound.Writer.TryWrite( buffered );
                    }
                }
            } else {
                _ = _subscriptions.TryRemove( key, out _ );

                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug( "Unsubscribed from output for task {TaskId} schedule {ScheduleId}.",
                        sub.TaskId, sub.ScheduleId );
                }
            }
        }
    }

    private async Task WriteOutputAsync(
        IClientStreamWriter<OutputMessage> writer,
        CancellationToken ct
    ) {
        await foreach (OutputMessage message in _outbound.Reader.ReadAllAsync( ct )) {
            await writer.WriteAsync( message, ct );
        }
    }

    // ── Ring Buffer ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Simple thread-safe bounded ring buffer that keeps the last N items.
    /// </summary>
    private sealed class BoundedBuffer( int capacity ) {
        private readonly OutputMessage[] _items = new OutputMessage[capacity];
        private readonly Lock _lock = new( );
        private int _head;
        private int _count;

        public void Add( OutputMessage item ) {
            lock (_lock) {
                _items[_head] = item;
                _head = (_head + 1) % capacity;
                if (_count < capacity) {
                    _count++;
                }
            }
        }

        public OutputMessage[] Snapshot( ) {
            lock (_lock) {
                OutputMessage[] snapshot = new OutputMessage[_count];
                int start = (_head - _count + capacity) % capacity;
                for (int i = 0; i < _count; i++) {
                    snapshot[i] = _items[(start + i) % capacity];
                }
                return snapshot;
            }
        }
    }
}
