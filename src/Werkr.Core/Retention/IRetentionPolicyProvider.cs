namespace Werkr.Core.Retention;

/// <summary>
/// Contract for a provider that can delete aged records of a specific entity type.
/// Each provider handles one <see cref="EntityType"/> and knows how to query and
/// purge records older than the configured retention period.
/// </summary>
public interface IRetentionPolicyProvider {

    /// <summary>
    /// The logical entity type this provider manages (e.g. "workflow_run", "audit_log").
    /// Must match the <c>EntityType</c> column in the <c>RetentionPolicy</c> table.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Deletes records older than <paramref name="retentionDays"/> in batches.
    /// Captures date range metadata before deletion for audit trail purposes.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain. Records older than this are eligible.</param>
    /// <param name="batchSize">Maximum records to delete in a single database round-trip.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the count of deleted records and the date range of the oldest/newest deleted.</returns>
    Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct );

    /// <summary>
    /// Returns a preview of how many records would be deleted without actually deleting them.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A preview summary of eligible records.</returns>
    Task<RetentionPreview> PreviewAgedRecordsAsync( int retentionDays, CancellationToken ct );
}
