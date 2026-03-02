using System.Collections.Concurrent;
using System.Threading.Channels;

using Werkr.Common.Models;

namespace Werkr.Core.Workflows;

/// <summary>
/// Manages <see cref="Channel{T}"/> instances per workflow run so that
/// the <see cref="WorkflowExecutor"/> can publish real-time step updates
/// and UI consumers can stream them via <see cref="IAsyncEnumerable{T}"/>.
/// Registered as a singleton.
/// </summary>
public sealed class WorkflowRunTracker {

    private readonly ConcurrentDictionary<Guid, Channel<WorkflowStepStatusUpdate>> _channels = new( );

    /// <summary>
    /// Creates and returns a <see cref="ChannelWriter{T}"/> for the given run.
    /// Called by <see cref="WorkflowExecutor"/> when a run begins.
    /// </summary>
    public ChannelWriter<WorkflowStepStatusUpdate> StartTracking( Guid runId ) {
        Channel<WorkflowStepStatusUpdate> channel = Channel.CreateUnbounded<WorkflowStepStatusUpdate>(
            new UnboundedChannelOptions { SingleWriter = true } );
        _ = _channels.TryAdd( runId, channel );
        return channel.Writer;
    }

    /// <summary>
    /// Completes the channel for a run and removes it from tracking.
    /// Called by <see cref="WorkflowExecutor"/> when the run ends.
    /// </summary>
    public void CompleteTracking( Guid runId ) {
        if (_channels.TryRemove( runId, out Channel<WorkflowStepStatusUpdate>? channel )) {
            _ = channel.Writer.TryComplete( );
        }
    }

    /// <summary>
    /// Returns an <see cref="IAsyncEnumerable{T}"/> that streams step updates
    /// for the given run. Returns null if the run is not being tracked.
    /// </summary>
    public IAsyncEnumerable<WorkflowStepStatusUpdate>? GetUpdates( Guid runId ) {
        return _channels.TryGetValue( runId, out Channel<WorkflowStepStatusUpdate>? channel ) ? channel.Reader.ReadAllAsync( ) : null;
    }

    /// <summary>Returns true if the run is currently being tracked.</summary>
    public bool IsTracking( Guid runId ) => _channels.ContainsKey( runId );
}
