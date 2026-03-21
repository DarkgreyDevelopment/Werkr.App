using System.Threading.Channels;
using Werkr.Agent.Communication;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Configuration;

/// <summary>
/// Background service that synchronizes configuration from the server.
/// Triggers: startup (full sync), push notification (delta sync), periodic fallback.
/// </summary>
public sealed partial class ConfigurationSyncBackgroundService(
    AgentConfigurationProvider configProvider,
    AgentGrpcClientFactory clientFactory,
    Channel<long> configChangeChannel,
    ILogger<ConfigurationSyncBackgroundService> logger
) : BackgroundService {

    private static readonly TimeSpan s_fallbackInterval = TimeSpan.FromMinutes( 5 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        // Load from local cache first (offline capability)
        await configProvider.LoadFromDatabaseAsync( stoppingToken );

        // Initial full sync
        await SyncAsync( fullSync: true, stoppingToken );

        // Listen for push notifications OR fallback timer
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( stoppingToken );
                cts.CancelAfter( s_fallbackInterval );

                try {
                    // Wait for push notification (or timeout for periodic sync)
                    _ = await configChangeChannel.Reader.ReadAsync( cts.Token );
                } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
                    // Fallback timer expired — do periodic sync
                }

                await SyncAsync( fullSync: false, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogSyncFailed( logger, ex );
                // Continue with cached values; retry on next interval
                await Task.Delay( TimeSpan.FromSeconds( 30 ), stoppingToken );
            }
        }
    }

    private async Task SyncAsync( bool fullSync, CancellationToken ct ) {
        try {
            if (!await clientFactory.IsRegisteredAsync( ct )) {
                LogNoConnection( logger );
                return;
            }

            RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );
            ConfigurationSync.ConfigurationSyncClient client =
                await clientFactory.CreateConfigurationSyncClientAsync( ct );

            ConfigSyncRequest request = new( ) {
                ConnectionId = connection.Id.ToString( ),
                LastKnownVersion = fullSync ? 0 : configProvider.LastKnownVersion,
            };

            byte[] sharedKey = clientFactory.GetSharedKey( );
            string keyId = clientFactory.GetKeyId( );
            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope( request, sharedKey, keyId );

            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions(
                timeout: TimeSpan.FromSeconds( 30 ), cancellationToken: ct );
            EncryptedEnvelope responseEnvelope = await client.GetConfigurationAsync( envelope, callOptions );

            ConfigSyncResponse response = PayloadEncryptor.DecryptFromEnvelope<ConfigSyncResponse>(
                responseEnvelope, sharedKey );

            if (response.Entries.Count > 0) {
                List<(string Key, string Value, string ValueType, string Category, long Version, bool Deleted)> entries = [];
                foreach (ConfigEntryMessage entry in response.Entries) {
                    entries.Add( (entry.Key, entry.Value, entry.ValueType, entry.Category, entry.Version, entry.Deleted) );
                }

                await configProvider.ApplySyncEntriesAsync( entries, response.CurrentVersion, ct );
            }

            if (response.Credentials.Count > 0) {
                List<(string Name, string Type, bool IsScopedToThisAgent)> credentials = [];
                foreach (CredentialMetadata cred in response.Credentials) {
                    credentials.Add( (cred.Name, cred.CredentialType, cred.IsScopedToThisAgent) );
                }

                await configProvider.UpdateCredentialMetadataAsync( credentials, ct );
            }

            LogSyncComplete( logger, fullSync ? "full" : "delta", response.Entries.Count, response.CurrentVersion );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogSyncFailed( logger, ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Config {SyncType} sync complete: {Count} entries, serverVersion={Version}" )]
    private static partial void LogSyncComplete( ILogger logger, string syncType, int count, long version );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Configuration sync failed. Continuing with cached values." )]
    private static partial void LogSyncFailed( ILogger logger, Exception ex );

    [LoggerMessage( Level = LogLevel.Debug, Message = "No active server connection — skipping config sync." )]
    private static partial void LogNoConnection( ILogger logger );
}
