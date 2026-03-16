using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Werkr.Core.Communication;

/// <summary>
/// Singleton broadcaster that fans out <see cref="WorkflowEvent"/> notifications to all
/// active SSE subscribers. Each subscriber receives its own bounded
/// <see cref="ChannelReader{T}"/> so slow consumers cannot block producers.
/// </summary>
/// <param name="logger">Logger instance.</param>
public sealed partial class WorkflowEventBroadcaster( ILogger<WorkflowEventBroadcaster> logger ) {

    private readonly Lock _lock = new( );
    private readonly List<ChannelWriter<WorkflowEvent>> _subscribers = [];
    private readonly ILogger<WorkflowEventBroadcaster> _logger = logger;

    /// <summary>
    /// Creates a new subscription. The caller reads from the returned
    /// <see cref="ChannelReader{T}"/> and must dispose the returned
    /// <see cref="WorkflowEventSubscription"/> when done.
    /// </summary>
    public WorkflowEventSubscription Subscribe( ) {
        Channel<WorkflowEvent> channel = Channel.CreateBounded<WorkflowEvent>(
            new BoundedChannelOptions( 512 ) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            } );

        int count;
        lock (_lock) {
            _subscribers.Add( channel.Writer );
            count = _subscribers.Count;
        }

        LogSubscribed( count );

        return new WorkflowEventSubscription( channel.Reader, ( ) => Unsubscribe( channel.Writer ) );
    }

    /// <summary>
    /// Publishes a <see cref="WorkflowEvent"/> to every active subscriber.
    /// Non-blocking — events are dropped for subscribers whose channel is full.
    /// </summary>
    public void Publish( WorkflowEvent workflowEvent ) {
        ChannelWriter<WorkflowEvent>[] snapshot;
        lock (_lock) {
            snapshot = [.. _subscribers];
        }

        int delivered = 0;
        foreach (ChannelWriter<WorkflowEvent> writer in snapshot) {
            if (writer.TryWrite( workflowEvent )) {
                delivered++;
            }
        }

        LogPublished( workflowEvent.WorkflowRunId, workflowEvent.GetType( ).Name, delivered, snapshot.Length );
    }

    private void Unsubscribe( ChannelWriter<WorkflowEvent> writer ) {
        int count;
        lock (_lock) {
            _ = _subscribers.Remove( writer );
            count = _subscribers.Count;
        }

        _ = writer.TryComplete( );
        LogUnsubscribed( count );
    }

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Workflow SSE subscriber added. Active subscribers: {Count}." )]
    private partial void LogSubscribed( int count );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Published {EventType} for run {RunId} to {Delivered}/{Total} subscribers." )]
    private partial void LogPublished( Guid runId, string eventType, int delivered, int total );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Workflow SSE subscriber removed. Active subscribers: {Count}." )]
    private partial void LogUnsubscribed( int count );
}
