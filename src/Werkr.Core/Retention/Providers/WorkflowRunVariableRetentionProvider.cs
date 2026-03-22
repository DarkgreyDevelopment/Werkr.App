using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for <see cref="WorkflowRunVariable"/> records.
/// Deletes variable version rows whose parent <see cref="WorkflowRun"/>
/// has reached a terminal state and whose <c>EndTime</c> is older than
/// the configured retention period. Variables from active runs are exempt.
/// </summary>
public sealed class WorkflowRunVariableRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <summary>Terminal workflow run statuses — variables from these runs are eligible.</summary>
    private static readonly WorkflowRunStatus[] s_terminalStatuses =
        [WorkflowRunStatus.Succeeded, WorkflowRunStatus.Failed, WorkflowRunStatus.Cancelled];

    /// <inheritdoc />
    public string EntityType => "variable_version";

    /// <inheritdoc />
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        // Capture date range before deletion for audit trail (uses owning run's EndTime)
        IQueryable<WorkflowRunVariable> eligible = db.WorkflowRunVariables
            .Where( v => db.WorkflowRuns
                .Any( r => r.Id == v.WorkflowRunId
                    && s_terminalStatuses.Contains( r.Status )
                    && r.EndTime != null
                    && r.EndTime < cutoff ) );

        IQueryable<DateTime?> runEndTimes = eligible
            .Select( v => db.WorkflowRuns
                .Where( r => r.Id == v.WorkflowRunId )
                .Select( r => r.EndTime )
                .FirstOrDefault( ) )
            .Distinct( );

        int eligibleCount = await eligible.CountAsync( ct );
        DateTime? oldest = eligibleCount > 0 ? await runEndTimes.MinAsync( ct ) : null;
        DateTime? newest = eligibleCount > 0 ? await runEndTimes.MaxAsync( ct ) : null;

        // Batch-delete in a loop to avoid locking large numbers of rows at once
        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.WorkflowRunVariables
                .Where( v => db.WorkflowRuns
                    .Any( r => r.Id == v.WorkflowRunId
                        && s_terminalStatuses.Contains( r.Status )
                        && r.EndTime != null
                        && r.EndTime < cutoff ) )
                .OrderBy( v => v.Id )
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

        IQueryable<WorkflowRunVariable> eligible = db.WorkflowRunVariables
            .Where( v => db.WorkflowRuns
                .Any( r => r.Id == v.WorkflowRunId
                    && s_terminalStatuses.Contains( r.Status )
                    && r.EndTime != null
                    && r.EndTime < cutoff ) );

        int count = await eligible.CountAsync( ct );

        // Use the owning run's EndTime for date range reporting
        DateTime? oldest = null;
        DateTime? newest = null;
        if (count > 0) {
            IQueryable<DateTime?> runEndTimes = eligible
                .Select( v => db.WorkflowRuns
                    .Where( r => r.Id == v.WorkflowRunId )
                    .Select( r => r.EndTime )
                    .FirstOrDefault( ) )
                .Distinct( );

            oldest = await runEndTimes.MinAsync( ct );
            newest = await runEndTimes.MaxAsync( ct );
        }

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
