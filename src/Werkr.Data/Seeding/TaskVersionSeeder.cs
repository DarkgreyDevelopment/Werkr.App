using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Data.Seeding;

/// <summary>
/// Seeds initial <see cref="TaskVersion"/> v1 records for existing tasks that predate the versioning feature.
/// Also backfills <see cref="WorkflowStep.TaskVersionId"/> on steps that reference versioned tasks.
/// Idempotent — safe to run on every startup.
/// </summary>
public static class TaskVersionSeeder {

    /// <summary>
    /// Seeds task versions for any tasks where <see cref="WerkrTask.CurrentVersionId"/> is null.
    /// </summary>
    /// <param name="services">The application's root <see cref="IServiceProvider"/>.</param>
    public static async Task SeedAsync( IServiceProvider services ) {
        using IServiceScope scope = services.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        ILogger logger = services.GetRequiredService<ILoggerFactory>( )
            .CreateLogger( "Werkr.Data.Seeding.TaskVersionSeeder" );

        List<WerkrTask> unversionedTasks = await db.Tasks
            .Where( t => t.CurrentVersionId == null )
            .ToListAsync( );

        if (unversionedTasks.Count == 0) {
            return;
        }

        foreach (WerkrTask task in unversionedTasks) {
            TaskDefinitionSnapshot snapshot = TaskDefinitionSnapshot.FromTask( task );
            TaskVersion version = new( ) {
                TaskId = task.Id,
                VersionNumber = 1,
                Definition = snapshot.ToJson( ),
                ChangeDescription = "Initial version (seeded)",
            };
            _ = db.TaskVersions.Add( version );
            _ = await db.SaveChangesAsync( );

            task.CurrentVersionId = version.Id;
            _ = await db.SaveChangesAsync( );
        }

        // Backfill WorkflowStep.TaskVersionId for steps that reference versioned tasks
        List<WorkflowStep> unversionedSteps = await db.WorkflowSteps
            .Where( s => s.TaskId != null && s.TaskVersionId == null )
            .ToListAsync( );

        if (unversionedSteps.Count > 0) {
            // Build lookup of TaskId → CurrentVersionId
            Dictionary<long, long> taskVersionMap = unversionedTasks
                .Where( t => t.CurrentVersionId.HasValue )
                .ToDictionary( t => t.Id, t => t.CurrentVersionId!.Value );

            // Also load any tasks that were already versioned (not in unversionedTasks)
            long[] missingTaskIds = [.. unversionedSteps
                .Where( s => s.TaskId.HasValue && !taskVersionMap.ContainsKey( s.TaskId.Value ) )
                .Select( s => s.TaskId!.Value )
                .Distinct( )];

            if (missingTaskIds.Length > 0) {
                List<WerkrTask> existingVersionedTasks = await db.Tasks
                    .Where( t => missingTaskIds.Contains( t.Id ) && t.CurrentVersionId != null )
                    .ToListAsync( );
                foreach (WerkrTask t in existingVersionedTasks) {
                    taskVersionMap[t.Id] = t.CurrentVersionId!.Value;
                }
            }

            foreach (WorkflowStep step in unversionedSteps) {
                if (step.TaskId.HasValue && taskVersionMap.TryGetValue( step.TaskId.Value, out long versionId )) {
                    step.TaskVersionId = versionId;
                }
            }

            _ = await db.SaveChangesAsync( );
        }

        LogSeeded( logger, unversionedTasks.Count, unversionedSteps.Count );
    }

    private static readonly Action<ILogger, int, int, Exception?> s_logSeeded =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId( 1, "TaskVersionsSeeded" ),
            "Seeded {TaskCount} task version(s) and backfilled {StepCount} workflow step(s)." );

    private static void LogSeeded( ILogger logger, int taskCount, int stepCount ) {
        s_logSeeded( logger, taskCount, stepCount, null );
    }
}
