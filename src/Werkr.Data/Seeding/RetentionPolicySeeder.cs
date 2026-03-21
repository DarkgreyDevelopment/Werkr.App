using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds default retention policies on application startup.
/// Idempotent — checks <see cref="WerkrDbContext.RetentionPolicies"/> via
/// <c>AnyAsync</c> before inserting.
/// </summary>
public static class RetentionPolicySeeder {

    /// <summary>
    /// Seeds default retention policies if no policies exist yet.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.RetentionPolicySeeder" );

        bool anyExist = await db.RetentionPolicies.AnyAsync( );
        if (anyExist) {
            // Upsert missing entity types for upgrades (new providers added in later releases)
            int upserted = await UpsertMissingPoliciesAsync( db );
            if (upserted > 0) {
                LogUpserted( logger, upserted );
            }
            return;
        }

        DateTime now = DateTime.UtcNow;

        RetentionPolicy[] defaults = [
            new RetentionPolicy {
                EntityType = "workflow_run",
                RetentionDays = 180,
                IsEnabled = true,
                ModifiedUtc = now,
                ModifiedByUserId = "system",
                Created = now,
                LastUpdated = now,
                Version = 1,
            },
            new RetentionPolicy {
                EntityType = "audit_log",
                RetentionDays = 365,
                IsEnabled = true,
                ModifiedUtc = now,
                ModifiedByUserId = "system",
                Created = now,
                LastUpdated = now,
                Version = 1,
            },
        ];

        db.RetentionPolicies.AddRange( defaults );
        _ = await db.SaveChangesAsync( );

        LogSeeded( logger, defaults.Length );
    }

    /// <summary>
    /// Inserts any seed policies that are missing from an existing database (upgrade path).
    /// </summary>
    private static async Task<int> UpsertMissingPoliciesAsync( WerkrDbContext db ) {
        HashSet<string> existingTypes = [.. await db.RetentionPolicies
            .Select( p => p.EntityType )
            .ToListAsync( )];

        DateTime now = DateTime.UtcNow;
        RetentionPolicy[] allDefaults = BuildAllDefaults( now );
        RetentionPolicy[] missing = [.. allDefaults.Where( p => !existingTypes.Contains( p.EntityType ) )];

        if (missing.Length > 0) {
            db.RetentionPolicies.AddRange( missing );
            _ = await db.SaveChangesAsync( );
        }

        return missing.Length;
    }

    private static RetentionPolicy[] BuildAllDefaults( DateTime now ) => [
        new RetentionPolicy {
            EntityType = "workflow_run",
            RetentionDays = 180,
            IsEnabled = true,
            ModifiedUtc = now,
            ModifiedByUserId = "system",
            Created = now,
            LastUpdated = now,
            Version = 1,
        },
        new RetentionPolicy {
            EntityType = "audit_log",
            RetentionDays = 365,
            IsEnabled = true,
            ModifiedUtc = now,
            ModifiedByUserId = "system",
            Created = now,
            LastUpdated = now,
            Version = 1,
        },
    ];

    private static readonly Action<ILogger, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 1, "RetentionPoliciesSeeded" ),
            "Seeded {Count} retention policies." );

    private static void LogSeeded( ILogger logger, int count ) {
        s_logSeeded( logger, count, null );
    }

    private static readonly Action<ILogger, int, Exception?> s_logUpserted =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 2, "RetentionPoliciesUpserted" ),
            "Upserted {Count} missing retention policies on upgrade." );

    private static void LogUpserted( ILogger logger, int count ) {
        s_logUpserted( logger, count, null );
    }
}
