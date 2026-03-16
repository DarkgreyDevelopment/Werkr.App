using System.Threading.Channels;

namespace Werkr.Core.Communication;

/// <summary>
/// Represents an active SSE subscription to workflow events.
/// Disposing removes the subscriber from the broadcaster.
/// </summary>
/// <param name="reader">Channel reader for this subscriber.</param>
/// <param name="unsubscribe">Callback to remove the subscriber from the broadcaster.</param>
public sealed class WorkflowEventSubscription( ChannelReader<WorkflowEvent> reader, Action unsubscribe ) : IDisposable {

    private readonly Action _unsubscribe = unsubscribe;
    private bool _disposed;

    /// <summary>The channel reader that delivers workflow events to this subscriber.</summary>
    public ChannelReader<WorkflowEvent> Reader { get; } = reader;

    /// <summary>Unsubscribes from the broadcaster.</summary>
    public void Dispose( ) {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _unsubscribe( );
    }
}
