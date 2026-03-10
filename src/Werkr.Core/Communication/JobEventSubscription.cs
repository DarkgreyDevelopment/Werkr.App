using System.Threading.Channels;

namespace Werkr.Core.Communication;

/// <summary>
/// Represents an active SSE subscription to job events.
/// Disposing or calling <see cref="Dispose"/> removes the subscriber from the broadcaster.
/// </summary>
/// <remarks>Initializes a new subscription.</remarks>
/// <param name="reader">Channel reader for this subscriber.</param>
/// <param name="unsubscribe">Callback to remove the subscriber from the broadcaster.</param>
public sealed class JobEventSubscription( ChannelReader<JobEvent> reader, Action unsubscribe ) : IDisposable {

    private readonly Action _unsubscribe = unsubscribe;
    private bool _disposed;

    /// <summary>The channel reader that delivers job events to this subscriber.</summary>
    public ChannelReader<JobEvent> Reader { get; } = reader;

    /// <summary>Unsubscribes from the broadcaster.</summary>
    public void Dispose( ) {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _unsubscribe( );
    }
}
