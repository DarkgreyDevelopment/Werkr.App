using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for <see cref="WorkflowRun"/> records.
/// Deletes workflow runs in terminal states whose <c>EndTime</c> is older
/// than the configured retention period. Runs in non-terminal states
/// (Running, Pending, Queued, Paused) are exempt regardless of age.
/// </summary>
public sealed class WorkflowRunRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <summary>Terminal statuses eligible for retention deletion.</summary>
    private static readonly WorkflowRunStatus[] s_terminalStatuses =
        [WorkflowRunStatus.Succeeded, WorkflowRunStatus.Failed, WorkflowRunStatus.Cancelled];

    /// <inheritdoc />
    public string EntityType => "workflow_run";

    /// <inheritdoc />
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        // Capture date range before deletion for audit trail
        IQueryable<WorkflowRun> eligible = db.WorkflowRuns
            .Where( r => s_terminalStatuses.Contains( r.Status ) && r.EndTime != null && r.EndTime < cutoff );

        DateTime? oldest = await eligible.MinAsync( r =>  r.EndTime, ct );
        DateTime? newest = await eligible.MaxAsync( r =>  r.EndTime, ct );

        // Batch-delete in a loop to avoid locking large numbers of rows at once
        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.WorkflowRuns
                .Where( r => s_terminalStatuses.Contains( r.Status ) && r.EndTime != null && r.EndTime < cutoff )
                .OrderBy( r => r.EndTime )
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

        IQueryable<WorkflowRun> eligible = db.WorkflowRuns
            .Where( r => s_terminalStatuses.Contains( r.Status ) && r.EndTime != null && r.EndTime < cutoff );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( r => r.EndTime, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( r => r.EndTime, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
