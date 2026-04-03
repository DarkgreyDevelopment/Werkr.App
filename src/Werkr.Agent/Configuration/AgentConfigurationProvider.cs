using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Agent.Configuration;

/// <summary>
/// Singleton in-memory cache of configuration entries for fast agent-side lookups.
/// Populated from local SQLite on startup and refreshed via gRPC sync.
/// </summary>
public sealed partial class AgentConfigurationProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentConfigurationProvider> logger
) {

    private readonly ConcurrentDictionary<string, string> _cache = new( StringComparer.OrdinalIgnoreCase );
    private readonly ConcurrentDictionary<string, (string Type, bool IsScopedToThisAgent)> _credentialMetadata = new( StringComparer.OrdinalIgnoreCase );

    /// <summary>Gets the last known sync version from the server.</summary>
    public long LastKnownVersion { get; private set; }

    /// <summary>
    /// Loads all cached configuration entries from local SQLite.
    /// Called once at startup for offline capability.
    /// </summary>
    public async Task LoadFromDatabaseAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        List<ConfigurationEntry> entries = await db.ConfigurationEntries
            .AsNoTracking( )
            .ToListAsync( ct );

        foreach (ConfigurationEntry entry in entries) {
            _cache[entry.Key] = entry.Value;
            if (entry.SyncVersion > LastKnownVersion) {
                LastKnownVersion = entry.SyncVersion;
            }
        }

        LogLoaded( logger, entries.Count, LastKnownVersion );
    }

    /// <summary>
    /// Upserts entries received from a gRPC sync into both in-memory cache and local SQLite.
    /// </summary>
    public async Task ApplySyncEntriesAsync(
        IReadOnlyList<(string Key, string Value, string ValueType, string Category, long Version, bool Deleted)> entries,
        long serverVersion,
        CancellationToken ct
    ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        foreach ((string key, string value, string valueType, string category, long version, bool deleted) in entries) {
            if (deleted) {
                _ = _cache.TryRemove( key, out _ );
                ConfigurationEntry? existing = await db.ConfigurationEntries
                    .FirstOrDefaultAsync( e => e.Key == key, ct );
                if (existing is not null) {
                    _ = db.ConfigurationEntries.Remove( existing );
                }
                continue;
            }

            _cache[key] = value;

            ConfigurationEntry? entity = await db.ConfigurationEntries
                .FirstOrDefaultAsync( e => e.Key == key, ct );

            if (entity is not null) {
                entity.Value = value;
                entity.SyncVersion = version;
                entity.ModifiedUtc = DateTime.UtcNow;
            } else {
                entity = new ConfigurationEntry {
                    Key = key,
                    Value = value,
                    ValueType = valueType,
                    Category = category,
                    SyncVersion = version,
                    DefaultValue = value,
                    CreatedUtc = DateTime.UtcNow,
                    ModifiedUtc = DateTime.UtcNow,
                    ModifiedByUserId = "system",
                };
                _ = db.ConfigurationEntries.Add( entity );
            }
        }

        _ = await db.SaveChangesAsync( ct );
        LastKnownVersion = serverVersion;

        LogSyncApplied( logger, entries.Count, serverVersion );
    }

    /// <summary>
    /// Replaces the in-memory credential metadata cache with the latest set from the server.
    /// This is metadata only (name, type, scope); actual secrets are resolved on-demand at dispatch.
    /// </summary>
    public Task UpdateCredentialMetadataAsync(
        IReadOnlyList<(string Name, string Type, bool IsScopedToThisAgent)> credentials,
        CancellationToken ct
    ) {
        _credentialMetadata.Clear( );
        foreach ((string name, string type, bool isScopedToThisAgent) in credentials) {
            if (!ct.IsCancellationRequested)
                _credentialMetadata[name] = (type, isScopedToThisAgent);
        }

        LogCredentialMetadataUpdated( logger, credentials.Count );
        return Task.CompletedTask;
    }

    /// <summary>Gets the current credential metadata as a read-only snapshot.</summary>
    public IReadOnlyDictionary<string, (string Type, bool IsScopedToThisAgent)> GetCredentialMetadata( ) =>
        _credentialMetadata.ToDictionary( kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase );

    /// <summary>Gets a typed configuration value by key.</summary>
    public T GetValue<T>( string key, T defaultValue = default! ) {
        if (!_cache.TryGetValue( key, out string? raw )) {
            return defaultValue;
        }

        try {
            return (T)Convert.ChangeType( raw, typeof( T ), CultureInfo.InvariantCulture );
        } catch {
            return defaultValue;
        }
    }

    /// <summary>Gets a raw string configuration value by key.</summary>
    public string? GetString( string key ) =>
        _cache.TryGetValue( key, out string? value ) ? value : null;

    [LoggerMessage( Level = LogLevel.Information, Message = "Loaded {Count} configuration entries from local cache (version={Version})" )]
    private static partial void LogLoaded( ILogger logger, int count, long version );

    [LoggerMessage( Level = LogLevel.Information, Message = "Applied {Count} sync entries (serverVersion={Version})" )]
    private static partial void LogSyncApplied( ILogger logger, int count, long version );

    [LoggerMessage( Level = LogLevel.Information, Message = "Updated credential metadata: {Count} credentials" )]
    private static partial void LogCredentialMetadataUpdated( ILogger logger, int count );
}
