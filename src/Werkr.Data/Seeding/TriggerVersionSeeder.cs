using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Triggers;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds initial <see cref="TriggerVersion"/> v1 records for existing triggers that predate the versioning feature.
/// Sets default <see cref="VersionBindingMode"/> to Latest.
/// Idempotent — safe to run on every startup.
/// </summary>
public static class TriggerVersionSeeder {

    /// <summary>
    /// Seeds trigger versions for any triggers where CurrentVersionId is null.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.TriggerVersionSeeder" );

        List<FileMonitorTrigger> unversionedTriggers = await db.FileMonitorTriggers
            .Where( t => t.CurrentVersionId == null )
            .ToListAsync( );

        if (unversionedTriggers.Count == 0) {
            return;
        }

        foreach (FileMonitorTrigger trigger in unversionedTriggers) {
            TriggerDefinitionSnapshot snapshot = TriggerDefinitionSnapshot.FromTrigger( trigger );
            TriggerVersion version = new( ) {
                TriggerId = trigger.Id,
                VersionNumber = 1,
                Definition = snapshot.ToJson( ),
                ChangeDescription = "Initial version (seeded)",
            };
            _ = db.TriggerVersions.Add( version );
            _ = await db.SaveChangesAsync( );

            trigger.CurrentVersionId = version.Id;
            _ = await db.SaveChangesAsync( );
        }

        LogSeeded( logger, unversionedTriggers.Count );
    }

    private static readonly Action<ILogger, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId( 3, "TriggerVersionsSeeded" ),
            "Seeded {TriggerCount} trigger version(s)." );

    private static void LogSeeded( ILogger logger, int triggerCount ) {
        s_logSeeded( logger, triggerCount, null );
    }
}
