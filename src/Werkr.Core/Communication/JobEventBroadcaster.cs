using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Werkr.Core.Communication;

/// <summary>
/// Singleton broadcaster that fans out <see cref="JobEvent"/> notifications to all
/// active SSE subscribers. Each subscriber receives its own bounded
/// <see cref="ChannelReader{T}"/> so slow consumers cannot block producers.
/// </summary>
public sealed partial class JobEventBroadcaster {

    private readonly Lock _lock = new( );
    private readonly List<ChannelWriter<JobEvent>> _subscribers = [];
    private readonly ILogger<JobEventBroadcaster> _logger;

    /// <summary>Initializes a new broadcaster.</summary>
    /// <param name="logger">Logger instance.</param>
    public JobEventBroadcaster( ILogger<JobEventBroadcaster> logger ) {
        _logger = logger;
    }

    /// <summary>
    /// Creates a new subscription. The caller reads from the returned
    /// <see cref="ChannelReader{T}"/> and must call <see cref="Unsubscribe"/>
    /// (or dispose the <see cref="JobEventSubscription"/>) when done.
    /// </summary>
    /// <returns>A subscription handle containing the reader and unsubscribe action.</returns>
    public JobEventSubscription Subscribe( ) {
        Channel<JobEvent> channel = Channel.CreateBounded<JobEvent>(
            new BoundedChannelOptions( 256 ) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            } );

        lock (_lock) {
            _subscribers.Add( channel.Writer );
        }

        LogSubscribed( _subscribers.Count );

        return new JobEventSubscription( channel.Reader, ( ) => Unsubscribe( channel.Writer ) );
    }

    /// <summary>
    /// Publishes a <see cref="JobEvent"/> to every active subscriber.
    /// Non-blocking — events are dropped for subscribers whose channel is full.
    /// </summary>
    /// <param name="jobEvent">The event to broadcast.</param>
    public void Publish( JobEvent jobEvent ) {
        ChannelWriter<JobEvent>[] snapshot;
        lock (_lock) {
            snapshot = [.. _subscribers];
        }

        int delivered = 0;
        foreach (ChannelWriter<JobEvent> writer in snapshot) {
            if (writer.TryWrite( jobEvent )) {
                delivered++;
            }
        }

        LogPublished( jobEvent.JobId, delivered, snapshot.Length );
    }

    /// <summary>
    /// Removes a subscriber's writer from the broadcast list and completes it.
    /// </summary>
    private void Unsubscribe( ChannelWriter<JobEvent> writer ) {
        lock (_lock) {
            _ = _subscribers.Remove( writer );
        }

        _ = writer.TryComplete( );
        LogUnsubscribed( _subscribers.Count );
    }

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "SSE subscriber added. Active subscribers: {Count}." )]
    private partial void LogSubscribed( int count );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Published JobEvent {JobId} to {Delivered}/{Total} subscribers." )]
    private partial void LogPublished( Guid jobId, int delivered, int total );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "SSE subscriber removed. Active subscribers: {Count}." )]
    private partial void LogUnsubscribed( int count );
}
