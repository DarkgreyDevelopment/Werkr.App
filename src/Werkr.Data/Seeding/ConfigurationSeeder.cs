using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Configuration;
using Werkr.Data.Entities.Settings;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds default configuration entries on application startup.
/// Idempotent — checks by key before inserting. Migrates values from
/// the legacy <see cref="ConfigurationSettings"/> row if present.
/// </summary>
public static class ConfigurationSeeder {

    /// <summary>
    /// Seeds all known configuration keys with default values.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.ConfigurationSeeder" );

        // Check if seeding already done
        bool anyExist = await db.ConfigurationEntries.AnyAsync( );
        if (anyExist) {
            // Upsert missing keys for upgrades (new keys added in later releases)
            int upserted = await UpsertMissingKeysAsync( db );
            if (upserted > 0) {
                LogUpserted( logger, upserted );
            }
            return;
        }

        // Try to read legacy settings for migration
#pragma warning disable CS0618 // Intentional: migrating values from obsolete ConfigurationSettings
        ConfigurationSettings? legacy = await db.Set<ConfigurationSettings>( ).FirstOrDefaultAsync( );
#pragma warning restore CS0618

        ConfigurationEntry[] entries = BuildDefaults( legacy );
        db.ConfigurationEntries.AddRange( entries );
        _ = await db.SaveChangesAsync( );

        LogSeeded( logger, entries.Length );
    }

#pragma warning disable CS0618 // Intentional: migrating values from obsolete ConfigurationSettings
    private static ConfigurationEntry[] BuildDefaults( ConfigurationSettings? legacy ) {
#pragma warning restore CS0618
        DateTime now = DateTime.UtcNow;
        const string system = "system";

        return [
            // ── Server ──
            Entry( "server.name", legacy?.ServerName ?? "Werkr Server", "string", "server",
                "Display name shown in the Blazor UI header.", null, now, system ),
            Entry( "server.allowRegistration", legacy?.AllowRegistration.ToString( ).ToLowerInvariant( ) ?? "true",
                "boolean", "server", "Whether new agent registrations are accepted.", null, now, system ),

            // ── Agent ──
            Entry( "agent.heartbeat.intervalSeconds", "30", "number", "agent",
                "Seconds between agent heartbeat probes.", """{"min":5}""", now, system ),
            Entry( "agent.heartbeat.missedThreshold", "3", "number", "agent",
                "Number of missed heartbeats before an agent is marked offline.", """{"min":1}""", now, system ),
            Entry( "agent.concurrency.maxTasks", "5", "number", "agent",
                "Maximum concurrent tasks an agent can execute.", """{"min":1}""", now, system ),
            Entry( "agent.output.maxSizeBytes", "10485760", "number", "agent",
                "Maximum size in bytes for agent output capture.", """{"min":0}""", now, system ),

            // ── Security ──
            Entry( "security.defaultKeySize", legacy?.DefaultKeySize.ToString( ) ?? "4096", "number", "security",
                "Default RSA key size in bits.", """{"min":2048,"max":8192}""", now, system ),
            Entry( "security.keyRotation.intervalHours", "168", "number", "security",
                "Hours between automatic key rotations.", null, now, system ),
            Entry( "security.keyRotation.gracePeriodMinutes", "5", "number", "security",
                "Minutes both old and new keys are valid during rotation.", null, now, system ),

            // ── Network ──
            Entry( "network.allowPrivateNetworks", "false", "boolean", "network",
                "Whether agents on private networks can register.", null, now, system ),

            // ── Workflow ──
            Entry( "workflow.timeout.defaultMinutes", "60", "number", "workflow",
                "Default workflow timeout in minutes.", """{"min":1}""", now, system ),

            // ── UI (server polling) ──
            Entry( "server.polling.intervalSeconds",
                legacy?.PollingIntervalSeconds.ToString( ) ?? "30", "number", "server",
                "Seconds between dashboard auto-refresh polls.",
                """{"min":5,"max":300}""", now, system ),
            Entry( "server.polling.runDetailIntervalSeconds",
                legacy?.RunDetailPollingIntervalSeconds.ToString( ) ?? "15", "number", "server",
                "Seconds between run-detail auto-refresh polls.",
                """{"min":5}""", now, system ),

            // ── Retention ──
            Entry( "retention.sweepIntervalMinutes", "1440", "number", "server",
                "Minutes between automatic retention sweep cycles.", """{"min":15}""", now, system ),

            // ── Modules (prep for 2.3) ──
            Entry( "modules.DefaultActions.enabled", "true", "boolean", "security",
                "Whether the built-in default action handlers are enabled.", null, now, system ),
            Entry( "modules.security.trustedPublisherKeys", "[]", "json", "security",
                "Trusted module publisher public keys (JSON array).", null, now, system ),
            Entry( "modules.security.allowUnsigned", "false", "boolean", "security",
                "Whether unsigned modules are allowed.", null, now, system ),
        ];
    }

    /// <summary>
    /// Inserts any seed keys that are missing from an existing database (upgrade path).
    /// </summary>
    private static async Task<int> UpsertMissingKeysAsync( WerkrDbContext db ) {
        HashSet<string> existingKeys = [.. await db.ConfigurationEntries
            .Where( e => e.ScopeLevel == 0 )
            .Select( e => e.Key )
            .ToListAsync( )];

        ConfigurationEntry[] allDefaults = BuildDefaults( null );
        ConfigurationEntry[] missing = [.. allDefaults.Where( e => !existingKeys.Contains( e.Key ) )];

        if (missing.Length > 0) {
            db.ConfigurationEntries.AddRange( missing );
            _ = await db.SaveChangesAsync( );
        }

        return missing.Length;
    }

    private static ConfigurationEntry Entry(
        string key, string value, string valueType, string category,
        string description, string? validationRules, DateTime now, string userId
    ) => new( ) {
        Key = key,
        Value = value,
        ValueType = valueType,
        Category = category,
        Description = description,
        ScopeLevel = 0,
        ScopeId = null,
        SyncVersion = 1,
        ValidationRules = validationRules,
        DefaultValue = value,
        CreatedUtc = now,
        ModifiedUtc = now,
        ModifiedByUserId = userId,
    };

    private static readonly Action<ILogger, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 1, "ConfigurationSeeded" ),
            "Seeded {Count} configuration entries." );

    private static void LogSeeded( ILogger logger, int count ) {
        s_logSeeded( logger, count, null );
    }

    private static readonly Action<ILogger, int, Exception?> s_logUpserted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 2, "ConfigurationUpserted" ),
            "Upserted {Count} missing configuration entries on upgrade." );

    private static void LogUpserted( ILogger logger, int count ) {
        s_logUpserted( logger, count, null );
    }
}
