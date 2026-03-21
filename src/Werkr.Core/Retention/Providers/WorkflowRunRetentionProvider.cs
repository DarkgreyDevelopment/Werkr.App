using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for <see cref="WorkflowRun"/> records.
/// Deletes completed (non-running) workflow runs whose <c>EndTime</c> is older
/// than the configured retention period.
/// </summary>
public sealed class WorkflowRunRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <inheritdoc />
    public string EntityType => "workflow_run";

    /// <inheritdoc />
    public async Task<int> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );
        int totalDeleted = 0;

        // Batch-delete in a loop to avoid locking large numbers of rows at once
        while (!ct.IsCancellationRequested) {
            int deleted = await db.WorkflowRuns
                .Where( r => r.Status != WorkflowRunStatus.Running && r.EndTime != null && r.EndTime < cutoff )
                .OrderBy( r => r.EndTime )
                .Take( batchSize )
                .ExecuteDeleteAsync( ct );

            totalDeleted += deleted;

            if (deleted < batchSize) {
                break;
            }
        }

        return totalDeleted;
    }

    /// <inheritdoc />
    public async Task<RetentionPreview> PreviewAgedRecordsAsync( int retentionDays, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<WorkflowRun> eligible = db.WorkflowRuns
            .Where( r => r.Status != WorkflowRunStatus.Running && r.EndTime != null && r.EndTime < cutoff );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( r => r.EndTime, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( r => r.EndTime, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
