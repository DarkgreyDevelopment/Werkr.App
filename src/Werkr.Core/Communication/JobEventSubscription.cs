using System.Threading.Channels;

namespace Werkr.Core.Communication;

/// <summary>
/// Represents an active SSE subscription to job events.
/// Disposing or calling <see cref="Dispose"/> removes the subscriber from the broadcaster.
/// </summary>
public sealed class JobEventSubscription : IDisposable {

    private readonly Action _unsubscribe;
    private bool _disposed;

    /// <summary>The channel reader that delivers job events to this subscriber.</summary>
    public ChannelReader<JobEvent> Reader { get; }

    /// <summary>Initializes a new subscription.</summary>
    /// <param name="reader">Channel reader for this subscriber.</param>
    /// <param name="unsubscribe">Callback to remove the subscriber from the broadcaster.</param>
    public JobEventSubscription( ChannelReader<JobEvent> reader, Action unsubscribe ) {
        Reader = reader;
        _unsubscribe = unsubscribe;
    }

    /// <summary>Unsubscribes from the broadcaster.</summary>
    public void Dispose( ) {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _unsubscribe( );
    }
}
