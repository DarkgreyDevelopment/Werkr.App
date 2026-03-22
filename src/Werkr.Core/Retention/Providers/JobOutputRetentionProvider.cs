using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for <see cref="WerkrJob"/> records (job output).
/// Deletes completed jobs whose <c>EndTime</c> is older than the configured
/// retention period. Jobs belonging to workflow runs in non-terminal states
/// are exempt to avoid deleting output for active workflows.
/// </summary>
public sealed class JobOutputRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <summary>Terminal workflow run statuses — jobs from these runs are eligible for retention.</summary>
    private static readonly WorkflowRunStatus[] s_terminalStatuses =
        [WorkflowRunStatus.Succeeded, WorkflowRunStatus.Failed, WorkflowRunStatus.Cancelled];

    /// <inheritdoc />
    public string EntityType => "job_output";

    /// <inheritdoc />
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        // Capture date range before deletion for audit trail
        IQueryable<WerkrJob> eligible = db.Jobs
            .Where( j => j.EndTime != null && j.EndTime < cutoff )
            .Where( j => j.WorkflowRunId == null
                || db.WorkflowRuns
                    .Where( r => r.Id == j.WorkflowRunId && s_terminalStatuses.Contains( r.Status ) )
                    .Any( ) );

        DateTime? oldest = await eligible.MinAsync( j => (DateTime?) j.EndTime, ct );
        DateTime? newest = await eligible.MaxAsync( j => (DateTime?) j.EndTime, ct );

        // Batch-delete in a loop to avoid locking large numbers of rows at once
        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.Jobs
                .Where( j => j.EndTime != null && j.EndTime < cutoff )
                .Where( j => j.WorkflowRunId == null
                    || db.WorkflowRuns
                        .Where( r => r.Id == j.WorkflowRunId && s_terminalStatuses.Contains( r.Status ) )
                        .Any( ) )
                .OrderBy( j => j.EndTime )
                .Take( batchSize )
                .ExecuteDeleteAsync( ct );

            totalDeleted += deleted;

            if (deleted < batchSize) {
                break;
            }
        }

        return new RetentionSweepResult( EntityType, totalDeleted, oldest, newest );
    }

    /// <inheritdoc />
    public async Task<RetentionPreview> PreviewAgedRecordsAsync( int retentionDays, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<WerkrJob> eligible = db.Jobs
            .Where( j => j.EndTime != null && j.EndTime < cutoff )
            .Where( j => j.WorkflowRunId == null
                || db.WorkflowRuns
                    .Where( r => r.Id == j.WorkflowRunId && s_terminalStatuses.Contains( r.Status ) )
                    .Any( ) );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( j => j.EndTime, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( j => j.EndTime, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
