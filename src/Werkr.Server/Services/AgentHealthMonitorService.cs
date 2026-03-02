using Werkr.Common.Models;

namespace Werkr.Server.Services;

/// <summary>
/// Background service that periodically checks agent health via the API's
/// <c>/api/agents/health</c> endpoint and updates each agent's persisted status
/// via <c>PUT /api/agents/{id}/status</c>. This keeps the DB status in sync
/// with actual reachability so all pages (not just the Dashboard) see accurate data.
/// </summary>
public sealed class AgentHealthMonitorService : BackgroundService {
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServerConfigCache _configCache;
    private readonly ILogger<AgentHealthMonitorService> _logger;

    /// <summary>Initializes the health monitor.</summary>
    public AgentHealthMonitorService(
        IHttpClientFactory httpClientFactory,
        ServerConfigCache configCache,
        ILogger<AgentHealthMonitorService> logger ) {
        _httpClientFactory = httpClientFactory;
        _configCache = configCache;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        // Wait briefly for the app to finish starting and for the system API key to be seeded.
        await Task.Delay( TimeSpan.FromSeconds( 15 ), stoppingToken );

        int intervalSeconds = _configCache.PollingIntervalSeconds;
        using PeriodicTimer timer = new( TimeSpan.FromSeconds( intervalSeconds ) );

        _logger.LogInformation( "AgentHealthMonitorService started." );

        while (await timer.WaitForNextTickAsync( stoppingToken )) {
            try {
                await PollAndUpdateAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogWarning( ex, "Health monitor poll failed." );
            }
        }
    }

    private async Task PollAndUpdateAsync( CancellationToken ct ) {
        HttpClient client = _httpClientFactory.CreateClient( "ApiService" );

        // Get live health from the API (which does real gRPC checks)
        List<AgentHealthDto>? healthResults = await client.GetFromJsonAsync<List<AgentHealthDto>>(
            "/api/agents/health", ct );

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
                    $"/api/agents/{health.AgentId}/status",
                    new UpdateAgentStatusRequest( newStatus ),
                    ct );

                if (!response.IsSuccessStatusCode) {
                    _logger.LogDebug( "Failed to update agent status." );
                }
            } catch (Exception ex) {
                _logger.LogDebug( ex, "Failed to update agent status." );
            }
        }
    }
}
