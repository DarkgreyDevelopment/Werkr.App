using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Configuration;
using Werkr.Data.Entities.Registration;

namespace Werkr.Data.Seeding;

/// <summary>
/// Migrates per-agent <see cref="RegisteredConnection.AllowedPaths"/> arrays into
/// <see cref="ConfigurationEntry"/> rows (ScopeLevel=1, agent-scoped), enabling
/// distribution via the configuration sync pipeline.
/// Idempotent — only creates entries for agents without an existing config entry.
/// </summary>
public static class PathAllowlistMigrationSeeder {

    /// <summary>
    /// Scans all registered connections with non-empty AllowedPaths and creates
    /// matching ConfigurationEntry rows if they don't already exist.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.PathAllowlistMigrationSeeder" );

#pragma warning disable CS0618 // Intentional: migrating values from obsolete AllowedPaths
        List<RegisteredConnection> agents = [.. (await db.RegisteredConnections
            .Where( c => c.IsServer )
            .ToListAsync( ))
            .Where( c => c.AllowedPaths.Length > 0 )];

        if (agents.Count == 0) {
            return;
        }

        // Find existing path allowlist config entries to avoid duplicates
        HashSet<string> existingScopeIds = [.. await db.ConfigurationEntries
            .Where( e => e.Key == "agent.pathAllowlist" && e.ScopeLevel == 1 )
            .Select( e => e.ScopeId! )
            .ToListAsync( )];

        long maxVersion = await db.ConfigurationEntries
            .MaxAsync( e => (long?)e.SyncVersion ) ?? 0;

        DateTime now = DateTime.UtcNow;
        int migrated = 0;

        foreach (RegisteredConnection agent in agents) {
            string agentId = agent.Id.ToString( );
            if (existingScopeIds.Contains( agentId )) {
                continue;
            }

            maxVersion++;
            _ = db.ConfigurationEntries.Add( new ConfigurationEntry {
                Key = "agent.pathAllowlist",
                Value = JsonSerializer.Serialize( agent.AllowedPaths ), // CS0618 suppressed above
#pragma warning restore CS0618
                ValueType = "json",
                Category = "agent",
                Description = "Allowed file system paths for action handlers (glob patterns).",
                ScopeLevel = 1,
                ScopeId = agentId,
                SyncVersion = maxVersion,
                DefaultValue = "[]",
                CreatedUtc = now,
                ModifiedUtc = now,
                ModifiedByUserId = "system",
            } );
            migrated++;
        }

        if (migrated > 0) {
            _ = await db.SaveChangesAsync( );
            LogMigrated( logger, migrated );
        }
    }

    private static readonly Action<ILogger, int, Exception?> s_logMigrated =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 1, "PathAllowlistMigrated" ),
            "Migrated {Count} agent path allowlists to ConfigurationEntry." );

    private static void LogMigrated( ILogger logger, int count ) {
        s_logMigrated( logger, count, null );
    }
}
