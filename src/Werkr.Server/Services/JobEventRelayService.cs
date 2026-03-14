using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Werkr.Server.Hubs;

namespace Werkr.Server.Services;

/// <summary>
/// Background service that subscribes to the API's SSE workflow event stream
/// and relays events to SignalR hub groups for real-time browser push.
/// Implements <see cref="IHealthCheck"/> to report SSE connection status.
/// </summary>
public sealed partial class JobEventRelayService(
    IHttpClientFactory httpClientFactory,
    IHubContext<WorkflowRunHub> hubContext,
    ILogger<JobEventRelayService> logger
    ) : BackgroundService, IHealthCheck {

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IHubContext<WorkflowRunHub> _hubContext = hubContext;
    private readonly ILogger<JobEventRelayService> _logger = logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Tracks whether the SSE connection is currently healthy.</summary>
    private volatile bool _isConnected;

    /// <summary>Active run IDs with at least one SignalR client subscribed.</summary>
    internal static ConcurrentDictionary<Guid, byte> ActiveRunIds { get; } = new( );

    /// <summary>Registers an active run ID (called by hub on JoinRun).</summary>
    internal static void TrackRun( Guid runId ) => ActiveRunIds.TryAdd( runId, 0 );

    /// <summary>Removes an active run ID (called by hub on LeaveRun).</summary>
    internal static void UntrackRun( Guid runId ) => ActiveRunIds.TryRemove( runId, out _ );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        // Brief startup delay to let the API come up.
        await Task.Delay( TimeSpan.FromSeconds( 5 ), stoppingToken );

        int backoffMs = 1000;
        const int MaxBackoffMs = 30_000;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                LogConnecting( _logger );
                await ConsumeStreamAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _isConnected = false;
                LogDisconnected( _logger, ex );

                await Task.Delay( backoffMs, stoppingToken );
                backoffMs = Math.Min( backoffMs * 2, MaxBackoffMs );
            }
        }
    }

    /// <summary>
    /// Opens the SSE stream and dispatches events to SignalR groups.
    /// </summary>
    private async Task ConsumeStreamAsync( CancellationToken ct ) {
        HttpClient client = _httpClientFactory.CreateClient( "ApiServiceSse" );

        using HttpRequestMessage request = new( HttpMethod.Get, "/api/events/workflow-runs" );
        request.Headers.Accept.Add( new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue( "text/event-stream" ) );

        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct );
        _ = response.EnsureSuccessStatusCode( );

        _isConnected = true;
        LogConnected( _logger );

        // Reset backoff on successful connect.
        using Stream stream = await response.Content.ReadAsStreamAsync( ct );
        using StreamReader reader = new( stream );

        string? currentEventType = null;
        string? currentData = null;

        while (!ct.IsCancellationRequested) {
            string? line = await reader.ReadLineAsync( ct );

            if (line is null) {
                // Stream ended — reconnect.
                break;
            }

            if (line.StartsWith( "event:", StringComparison.Ordinal )) {
                currentEventType = line[6..].Trim( );
            } else if (line.StartsWith( "data:", StringComparison.Ordinal )) {
                currentData = line[5..].Trim( );
            } else if (line.Length == 0 && currentEventType is not null && currentData is not null) {
                // End of SSE frame — dispatch.
                await DispatchEventAsync( currentEventType, currentData, ct );
                currentEventType = null;
                currentData = null;
            }
        }
    }

    /// <summary>
    /// Routes a parsed SSE event to the appropriate SignalR hub group.
    /// </summary>
    private async Task DispatchEventAsync( string eventType, string data, CancellationToken ct ) {
        try {
            using JsonDocument doc = JsonDocument.Parse( data );
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty( "workflowRunId", out JsonElement runIdElement )) {
                return;
            }

            string? runIdStr = runIdElement.GetString( );
            if (!Guid.TryParse( runIdStr, out Guid runId )) {
                return;
            }

            string group = runId.ToString( );

            switch (eventType) {
                case "step-started":
                    await _hubContext.Clients.Group( group ).SendAsync( "StepStatusChanged",
                        new StepStatusDto(
                            runId,
                            root.GetProperty( "stepId" ).GetInt64( ),
                            root.GetProperty( "stepName" ).GetString( ) ?? "",
                            "Started",
                            null, null, null, null,
                            ParseTimestamp( root )
                        ), ct );
                    break;

                case "step-completed":
                    await _hubContext.Clients.Group( group ).SendAsync( "StepStatusChanged",
                        new StepStatusDto(
                            runId,
                            root.GetProperty( "stepId" ).GetInt64( ),
                            root.GetProperty( "stepName" ).GetString( ) ?? "",
                            "Completed",
                            ParseGuid( root, "jobId" ),
                            root.GetProperty( "exitCode" ).GetInt32( ),
                            root.GetProperty( "runtimeSeconds" ).GetDouble( ),
                            null,
                            ParseTimestamp( root )
                        ), ct );
                    break;

                case "step-failed":
                    await _hubContext.Clients.Group( group ).SendAsync( "StepStatusChanged",
                        new StepStatusDto(
                            runId,
                            root.GetProperty( "stepId" ).GetInt64( ),
                            root.GetProperty( "stepName" ).GetString( ) ?? "",
                            "Failed",
                            ParseGuid( root, "jobId" ),
                            root.GetProperty( "exitCode" ).GetInt32( ),
                            null,
                            root.GetProperty( "errorMessage" ).GetString( ),
                            ParseTimestamp( root )
                        ), ct );
                    break;

                case "step-skipped":
                    await _hubContext.Clients.Group( group ).SendAsync( "StepStatusChanged",
                        new StepStatusDto(
                            runId,
                            root.GetProperty( "stepId" ).GetInt64( ),
                            root.GetProperty( "stepName" ).GetString( ) ?? "",
                            "Skipped",
                            null, null, null,
                            root.GetProperty( "reason" ).GetString( ),
                            ParseTimestamp( root )
                        ), ct );
                    break;

                case "run-completed": {
                        bool success = root.GetProperty( "success" ).GetBoolean( );
                        await _hubContext.Clients.Group( group ).SendAsync( "RunStatusChanged",
                            new RunStatusDto(
                                runId,
                                success ? "Completed" : "Failed",
                                null,
                                root.TryGetProperty( "failedStepId", out JsonElement fsId ) && fsId.ValueKind != JsonValueKind.Null
                                    ? fsId.GetInt64( )
                                    : null,
                                ParseTimestamp( root )
                            ), ct );
                        break;
                    }

                case "log-appended":
                    await _hubContext.Clients.Group( group ).SendAsync( "LogAppended",
                        new LogLineDto(
                            runId,
                            root.GetProperty( "stepId" ).GetInt64( ),
                            ParseGuid( root, "jobId" ) ?? Guid.Empty,
                            root.GetProperty( "line" ).GetString( ) ?? "",
                            ParseTimestamp( root )
                        ), ct );
                    break;

                default:
                    LogUnknownEventType( _logger, eventType );
                    break;
            }
        } catch (Exception ex) {
            LogDispatchFailed( _logger, eventType, ex );
        }
    }

    private static DateTime ParseTimestamp( JsonElement root ) {
        return root.TryGetProperty( "timestamp", out JsonElement ts ) && ts.TryGetDateTime( out DateTime dt ) ? dt : DateTime.UtcNow;
    }

    private static Guid? ParseGuid( JsonElement root, string propertyName ) {
        return root.TryGetProperty( propertyName, out JsonElement el ) &&
            el.ValueKind == JsonValueKind.String &&
            Guid.TryParse( el.GetString( ), out Guid g )
            ? g
            : null;
    }

    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default ) {
        return Task.FromResult( _isConnected
            ? HealthCheckResult.Healthy( "SSE stream connected." )
            : HealthCheckResult.Degraded( "SSE stream disconnected — reconnecting." ) );
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "JobEventRelayService connecting to SSE stream..." )]
    private static partial void LogConnecting( ILogger logger );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "JobEventRelayService connected to SSE stream." )]
    private static partial void LogConnected( ILogger logger );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "JobEventRelayService SSE stream disconnected." )]
    private static partial void LogDisconnected( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Unknown SSE event type: {EventType}" )]
    private static partial void LogUnknownEventType( ILogger logger, string eventType );

    [LoggerMessage( Level = LogLevel.Error,
        Message = "Failed to dispatch SSE event of type {EventType} to SignalR." )]
    private static partial void LogDispatchFailed( ILogger logger, string eventType, Exception ex );
}
