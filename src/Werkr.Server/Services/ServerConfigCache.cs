using Microsoft.EntityFrameworkCore;

using Werkr.Data.Identity;
using Werkr.Data.Identity.Entities;

namespace Werkr.Server.Services;

/// <summary>
/// Singleton cache of the <see cref="ConfigurationSettings"/> row stored in the database.
/// Provides fast, synchronous property access for UI polling intervals and server identity.
/// <para>
/// The cache is loaded once during startup via <see cref="InitializeAsync"/> (called from
/// <c>Program.cs</c> before the identity seeder runs) and can be refreshed on demand.
/// </para>
/// </summary>
public sealed class ServerConfigCache {
    private readonly IServiceProvider _services;
    private readonly ILogger<ServerConfigCache> _logger;
    private volatile ConfigurationSettings _config = new( );

    /// <summary>Creates a new instance backed by the application service provider.</summary>
    public ServerConfigCache( IServiceProvider services, ILogger<ServerConfigCache> logger ) {
        _services = services;
        _logger = logger;
    }

    // ── Synchronous property accessors (hot path) ────────────────────

    /// <summary>Dashboard / list auto-refresh interval in seconds (minimum 10).</summary>
    public int PollingIntervalSeconds => Math.Max( _config.PollingIntervalSeconds, 10 );

    /// <summary>Run-detail auto-refresh interval in seconds (minimum 5).</summary>
    public int RunDetailPollingIntervalSeconds => Math.Max( _config.RunDetailPollingIntervalSeconds, 5 );

    /// <summary>Display name for the Blazor UI header.</summary>
    public string ServerName => _config.ServerName;

    /// <summary>Whether new agent registrations are accepted.</summary>
    public bool AllowRegistration => _config.AllowRegistration;

    // ── Lifecycle ────────────────────────────────────────────────────

    /// <summary>
    /// Load config from the database, creating a default row if none exists.
    /// Called once from <c>Program.cs</c> after DB migration and before the identity seeder.
    /// </summary>
    public async Task InitializeAsync( CancellationToken ct = default ) {
        using IServiceScope scope = _services.CreateScope( );
        WerkrIdentityDbContext db = scope.ServiceProvider.GetRequiredService<WerkrIdentityDbContext>( );

        ConfigurationSettings? config = await db.ConfigurationSettings.FirstOrDefaultAsync( ct );
        if (config is null) {
            config = new ConfigurationSettings {
                Created = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
            };
            _ = db.ConfigurationSettings.Add( config );
            _ = await db.SaveChangesAsync( ct );
            _logger.LogInformation( "Seeded default server configuration." );
        }

        _config = config;
    }

    /// <summary>Reload the cached configuration from the database.</summary>
    public async Task RefreshAsync( CancellationToken ct = default ) {
        using IServiceScope scope = _services.CreateScope( );
        WerkrIdentityDbContext db = scope.ServiceProvider.GetRequiredService<WerkrIdentityDbContext>( );
        _config = await db.ConfigurationSettings.FirstOrDefaultAsync( ct ) ?? new( );
    }
}
