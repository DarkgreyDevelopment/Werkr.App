using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds initial <see cref="WorkflowVersion"/> v1 records for existing workflows that predate the versioning feature.
/// Also backfills <see cref="WorkflowRun.WorkflowVersionId"/> on runs that reference versioned workflows.
/// Idempotent — safe to run on every startup.
/// </summary>
public static class WorkflowVersionSeeder {

    /// <summary>
    /// Seeds workflow versions for any workflows where <see cref="Workflow.CurrentVersionId"/> is null.
    /// </summary>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.WorkflowVersionSeeder" );

        List<Workflow> unversionedWorkflows = await db.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Variables )
            .Where( w => w.CurrentVersionId == null )
            .ToListAsync( );

        if (unversionedWorkflows.Count == 0) {
            return;
        }

        foreach (Workflow workflow in unversionedWorkflows) {
            WorkflowDefinitionSnapshot snapshot = WorkflowDefinitionSnapshot.FromWorkflow( workflow );
            WorkflowVersion version = new( ) {
                WorkflowId = workflow.Id,
                VersionNumber = 1,
                Definition = snapshot.ToJson( ),
                ChangeDescription = "Initial version (seeded)",
            };
            _ = db.WorkflowVersions.Add( version );
            _ = await db.SaveChangesAsync( );

            workflow.CurrentVersionId = version.Id;
            _ = await db.SaveChangesAsync( );
        }

        // Backfill WorkflowRun.WorkflowVersionId for runs that reference versioned workflows
        List<WorkflowRun> unversionedRuns = await db.WorkflowRuns
            .Where( r => r.WorkflowVersionId == null )
            .ToListAsync( );

        if (unversionedRuns.Count > 0) {
            Dictionary<long, long> workflowVersionMap = unversionedWorkflows
                .Where( w => w.CurrentVersionId.HasValue )
                .ToDictionary( w => w.Id, w => w.CurrentVersionId!.Value );

            // Also load any workflows that were already versioned
            long[] missingWorkflowIds = [.. unversionedRuns
                .Where( r => !workflowVersionMap.ContainsKey( r.WorkflowId ) )
                .Select( r => r.WorkflowId )
                .Distinct( )];

            if (missingWorkflowIds.Length > 0) {
                List<Workflow> existingVersioned = await db.Workflows
                    .Where( w => missingWorkflowIds.Contains( w.Id ) && w.CurrentVersionId != null )
                    .ToListAsync( );
                foreach (Workflow w in existingVersioned) {
                    workflowVersionMap[w.Id] = w.CurrentVersionId!.Value;
                }
            }

            foreach (WorkflowRun run in unversionedRuns) {
                if (workflowVersionMap.TryGetValue( run.WorkflowId, out long versionId )) {
                    run.WorkflowVersionId = versionId;
                }
            }

            _ = await db.SaveChangesAsync( );
        }

        LogSeeded( logger, unversionedWorkflows.Count, unversionedRuns.Count );
    }

    private static readonly Action<ILogger, int, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId( 2, "WorkflowVersionsSeeded" ),
            "Seeded {WorkflowCount} workflow version(s) and backfilled {RunCount} workflow run(s)." );

    private static void LogSeeded( ILogger logger, int workflowCount, int runCount ) {
        s_logSeeded( logger, workflowCount, runCount, null );
    }
}
