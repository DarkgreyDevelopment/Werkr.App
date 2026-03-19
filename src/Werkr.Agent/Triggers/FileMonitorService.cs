using System.Collections.Concurrent;
using System.Threading.Channels;
using Werkr.Agent.Communication;
using Werkr.Common.Protos;
using Werkr.Core.Security;

namespace Werkr.Agent.Triggers;

/// <summary>
/// Background service that manages <see cref="FileSystemWatcher"/> instances for file monitor triggers.
/// Receives trigger definitions during schedule sync, validates watch directories against the path
/// allowlist, debounces rapid file events, and reports valid events to the API via
/// <see cref="TriggerEventClient"/>.
/// </summary>
/// <param name="triggerEventClient">Client for reporting file events to the API.</param>
/// <param name="pathValidator">Path allowlist validator for security enforcement.</param>
/// <param name="logger">Logger instance.</param>
public sealed class FileMonitorService(
    TriggerEventClient triggerEventClient,
    IPathAllowlistValidator pathValidator,
    ILogger<FileMonitorService> logger
) : BackgroundService {

    /// <summary>Active watchers keyed by trigger ID.</summary>
    private readonly ConcurrentDictionary<long, WatcherState> _watchers = new( );

    /// <summary>Last event time per file path for debouncing.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEventTimes = new( );

    /// <summary>Channel for decoupling watcher events from processing.</summary>
    private readonly Channel<FileEvent> _eventChannel = Channel.CreateUnbounded<FileEvent>(
        new UnboundedChannelOptions { SingleReader = true } );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "FileMonitorService started. Waiting for trigger definitions." );
        }

        try {
            await foreach (FileEvent evt in _eventChannel.Reader.ReadAllAsync( stoppingToken )) {
                try {
                    await ProcessFileEventAsync( evt, stoppingToken );
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    break;
                } catch (Exception ex) {
                    logger.LogError( ex, "Error processing file event for trigger {TriggerId}.", evt.TriggerId );
                }
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Expected on shutdown
        }

        // Cleanup all watchers on shutdown
        DisposeAllWatchers( );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "FileMonitorService stopped." );
        }
    }

    /// <summary>
    /// Reconciles the set of active file watchers with the provided trigger definitions.
    /// Adds new watchers, removes stale ones, and updates changed configurations.
    /// </summary>
    /// <param name="triggers">The current set of trigger definitions from the server.</param>
    public void ReconcileWatchers( IReadOnlyList<FileMonitorTriggerDef> triggers ) {
        HashSet<long> desiredIds = new( triggers.Select( t => t.TriggerId ) );

        // Remove watchers no longer in the server's list
        foreach (long existingId in _watchers.Keys) {
            if (!desiredIds.Contains( existingId )) {
                if (_watchers.TryRemove( existingId, out WatcherState? removed )) {
                    removed.Watcher.Dispose( );
                    if (logger.IsEnabled( LogLevel.Information )) {
                        logger.LogInformation( "Removed file watcher for trigger {TriggerId}.", existingId );
                    }
                }
            }
        }

        // Add or update watchers
        foreach (FileMonitorTriggerDef trigger in triggers) {
            try {
                // Validate path against allowlist
                if (!pathValidator.IsPathAllowed( trigger.WatchDirectory )) {
                    logger.LogWarning(
                        "Watch directory '{Directory}' for trigger {TriggerId} is outside the path allowlist. Skipping.",
                        trigger.WatchDirectory, trigger.TriggerId );
                    continue;
                }

                if (!Directory.Exists( trigger.WatchDirectory )) {
                    logger.LogWarning(
                        "Watch directory '{Directory}' for trigger {TriggerId} does not exist. Skipping.",
                        trigger.WatchDirectory, trigger.TriggerId );
                    continue;
                }

                if (_watchers.TryGetValue( trigger.TriggerId, out WatcherState? existing )) {
                    // Check if config changed
                    if (existing.WatchDirectory == trigger.WatchDirectory
                        && existing.FilePattern == trigger.FilePattern
                        && existing.DebounceMs == trigger.DebounceMs) {
                        continue; // No change
                    }

                    // Config changed — remove old watcher and create new one
                    existing.Watcher.Dispose( );
                    _ = _watchers.TryRemove( trigger.TriggerId, out _ );
                }

                CreateWatcher( trigger );
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to create file watcher for trigger {TriggerId}.", trigger.TriggerId );
            }
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "File monitor reconciliation complete. {Count} active watchers.", _watchers.Count );
        }
    }

    /// <summary>
    /// Creates a <see cref="FileSystemWatcher"/> for the given trigger definition.
    /// </summary>
    private void CreateWatcher( FileMonitorTriggerDef trigger ) {
        FileSystemWatcher watcher = new( trigger.WatchDirectory ) {
            Filter = trigger.FilePattern,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };

        HashSet<string> eventTypes = new( trigger.EventTypes, StringComparer.OrdinalIgnoreCase );

        if (eventTypes.Contains( "created" )) {
            watcher.Created += ( _, e ) => EnqueueEvent( trigger, e.FullPath, "created" );
        }

        if (eventTypes.Contains( "changed" )) {
            watcher.Changed += ( _, e ) => EnqueueEvent( trigger, e.FullPath, "changed" );
        }

        if (eventTypes.Contains( "deleted" )) {
            watcher.Deleted += ( _, e ) => EnqueueEvent( trigger, e.FullPath, "deleted" );
        }

        if (eventTypes.Contains( "renamed" )) {
            watcher.Renamed += ( _, e ) => EnqueueEvent( trigger, e.FullPath, "renamed" );
        }

        watcher.Error += ( _, e ) => OnWatcherError( trigger, e );

        WatcherState state = new(
            watcher,
            trigger.WatchDirectory,
            trigger.FilePattern,
            trigger.DebounceMs );

        _ = _watchers.TryAdd( trigger.TriggerId, state );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created file watcher for trigger {TriggerId}: directory='{Directory}', pattern='{Pattern}', events=[{Events}].",
                trigger.TriggerId, trigger.WatchDirectory, trigger.FilePattern,
                string.Join( ",", eventTypes ) );
        }
    }

    /// <summary>
    /// Enqueues a file event into the processing channel with debounce check.
    /// </summary>
    private void EnqueueEvent( FileMonitorTriggerDef trigger, string filePath, string eventType ) {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string debounceKey = $"{trigger.TriggerId}:{filePath}";

        if (_lastEventTimes.TryGetValue( debounceKey, out DateTimeOffset lastTime )) {
            if ((now - lastTime).TotalMilliseconds < trigger.DebounceMs) {
                return; // Debounced
            }
        }

        _lastEventTimes[debounceKey] = now;

        FileEvent evt = new(
            trigger.TriggerId,
            filePath,
            eventType,
            trigger.WatchDirectory,
            now.ToString( "o" ) );

        if (!_eventChannel.Writer.TryWrite( evt )) {
            logger.LogWarning(
                "Failed to enqueue file event for trigger {TriggerId}. Channel may be full.",
                trigger.TriggerId );
        }
    }

    /// <summary>
    /// Handles FileSystemWatcher error events by logging and attempting to restart the watcher.
    /// </summary>
    private void OnWatcherError( FileMonitorTriggerDef trigger, ErrorEventArgs e ) {
        logger.LogError( e.GetException( ),
            "FileSystemWatcher error for trigger {TriggerId} watching '{Directory}'. Attempting restart.",
            trigger.TriggerId, trigger.WatchDirectory );

        // Try to restart: remove old watcher and create new one
        if (_watchers.TryRemove( trigger.TriggerId, out WatcherState? old )) {
            old.Watcher.Dispose( );
        }

        try {
            if (Directory.Exists( trigger.WatchDirectory )) {
                CreateWatcher( trigger );
            } else {
                logger.LogWarning(
                    "Watch directory '{Directory}' no longer exists. Trigger {TriggerId} will not restart.",
                    trigger.WatchDirectory, trigger.TriggerId );
            }
        } catch (Exception ex) {
            logger.LogError( ex, "Failed to restart watcher for trigger {TriggerId}.", trigger.TriggerId );
        }
    }

    /// <summary>
    /// Processes a file event by reporting it to the API.
    /// </summary>
    private async Task ProcessFileEventAsync( FileEvent evt, CancellationToken ct ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Processing file event: trigger={TriggerId}, type={EventType}, path='{FilePath}'.",
                evt.TriggerId, evt.EventType, evt.FilePath );
        }

        _ = await triggerEventClient.ReportFileMonitorEventAsync(
            evt.TriggerId,
            evt.FilePath,
            evt.EventType,
            evt.Directory,
            evt.Timestamp,
            ct );
    }

    /// <summary>
    /// Disposes all active watchers.
    /// </summary>
    private void DisposeAllWatchers( ) {
        foreach (KeyValuePair<long, WatcherState> kvp in _watchers) {
            kvp.Value.Watcher.Dispose( );
        }
        _watchers.Clear( );
    }

    /// <summary>Internal state for a single file watcher.</summary>
    private sealed record WatcherState(
        FileSystemWatcher Watcher,
        string WatchDirectory,
        string FilePattern,
        int DebounceMs );

    /// <summary>Represents a file event to be processed.</summary>
    private sealed record FileEvent(
        long TriggerId,
        string FilePath,
        string EventType,
        string Directory,
        string Timestamp );
}
