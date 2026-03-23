using Werkr.Common.Models;

namespace Werkr.Server.Services;

/// <summary>
/// Background service that periodically checks agent health via the API's
/// <c>/api/agents/health</c> endpoint and updates each agent's persisted status
/// via <c>PUT /api/agents/{id}/status</c>. This keeps the DB status in sync
/// with actual reachability so all pages (not just the Dashboard) see accurate data.
/// </summary>
/// <remarks>Initializes the health monitor.</remarks>
public sealed partial class AgentHealthMonitorService(
    IHttpClientFactory httpClientFactory,
    ServerConfigCache configCache,
    ILogger<AgentHealthMonitorService> logger
    ) : BackgroundService {
    /// <summary>
    /// Factory used to create instances of the <c>"ApiServiceSystem"</c> named HTTP client which has the <see cref="Identity.ServiceAuthForwardingHandler"/> in its pipeline.
    /// </summary>
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    /// <summary>
    /// Cached server configuration from which the polling interval is read.
    /// </summary>
    private readonly ServerConfigCache _configCache = configCache;
    /// <summary>
    /// Logger for informational, warning, and debug messages.
    /// </summary>
    private readonly ILogger<AgentHealthMonitorService> _logger = logger;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        // Wait briefly for the app to finish starting and for the system API key to be seeded.
        await Task.Delay( TimeSpan.FromSeconds( 15 ), stoppingToken );

        int intervalSeconds = _configCache.PollingIntervalSeconds;
        using PeriodicTimer timer = new( TimeSpan.FromSeconds( intervalSeconds ) );

        LogServiceStarted( _logger );

        while (await timer.WaitForNextTickAsync( stoppingToken )) {
            try {
                await PollAndUpdateAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogPollFailed( _logger, ex );
            }
        }
    }

    /// <summary>
    /// Fetches the latest agent health data from <c>/api/agents/health</c> on the <c>Werkr.Api</c> and PUTs an updated status for each agent whose health result indicates a changed connection state. Unrecognised status strings are silently skipped.
    /// </summary>
    private async Task PollAndUpdateAsync( CancellationToken ct ) {
        HttpClient client = _httpClientFactory.CreateClient( "ApiServiceSystem" );

        // Get live health from the API (which does real gRPC checks)
        List<AgentHealthDto>? healthResults = await client.GetFromJsonAsync<List<AgentHealthDto>>(
            "/api/v1/agents/health", ct
        );

        if (healthResults is null || healthResults.Count == 0) {
            return;
        }

        foreach (AgentHealthDto health in healthResults) {
            // Map the health check result to a ConnectionStatus value
            string newStatus = health.Status switch {
                "Connected" => "Connected",
                "Unreachable" => "Disconnected",
                "Error" => "Error",
                _ => health.Status // pass through Disconnected, Revoked, etc.
            };

            // Only update if the status maps to a valid ConnectionStatus enum value
            if (!Enum.TryParse<ConnectionStatus>( newStatus, ignoreCase: true, out _ )) {
                continue;
            }

            try {
                using HttpResponseMessage response = await client.PutAsJsonAsync(
                    $"/api/v1/agents/{health.AgentId}/status",
                    new UpdateAgentStatusRequest( newStatus ),
                    ct
                );

                if (!response.IsSuccessStatusCode) {
                    LogStatusUpdateFailed( _logger );
                }
            } catch (Exception ex) {
                LogStatusUpdateFailedEx( _logger, ex );
            }
        }
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "AgentHealthMonitorService started." )]
    private static partial void LogServiceStarted( ILogger logger );

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Health monitor poll failed." )]
    private static partial void LogPollFailed( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Failed to update agent status." )]
    private static partial void LogStatusUpdateFailed( ILogger logger );

    [LoggerMessage( Level = LogLevel.Debug,
        Message = "Failed to update agent status." )]
    private static partial void LogStatusUpdateFailedEx( ILogger logger, Exception ex );
}
