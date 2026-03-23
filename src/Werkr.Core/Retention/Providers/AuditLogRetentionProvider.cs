using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Audit;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for <see cref="AuditEvent"/> records.
/// Deletes audit events whose <c>TimestampUtc</c> is older than the configured
/// retention period. Audit logging of the deletion is handled by the
/// <c>RetentionService</c> within the same transaction.
/// </summary>
public sealed class AuditLogRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <inheritdoc />
    public string EntityType => "audit_log";

    /// <inheritdoc />
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        // Gather summary before deletion — date range and category breakdown
        IQueryable<AuditEvent> expiredQuery = db.AuditEvents.Where( e => e.TimestampUtc < cutoff );
        int countToDelete = await expiredQuery.CountAsync( ct );

        if (countToDelete == 0) {
            return new RetentionSweepResult( EntityType, 0, null, null );
        }

        DateTime? earliest = await expiredQuery.MinAsync( e => (DateTime?) e.TimestampUtc, ct );
        DateTime? latest = await expiredQuery.MaxAsync( e => (DateTime?) e.TimestampUtc, ct );

        Dictionary<string, int> categoryCounts = await expiredQuery
            .GroupBy( e => e.EventCategory )
            .Select( g => new { Category = g.Key, Count = g.Count( ) } )
            .ToDictionaryAsync( x => x.Category, x => x.Count, ct );

        // Batch-delete
        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.AuditEvents
                .Where( e => e.TimestampUtc < cutoff )
                .OrderBy( e => e.TimestampUtc )
                .Take( batchSize )
                .ExecuteDeleteAsync( ct );

            totalDeleted += deleted;

            if (deleted < batchSize) {
                break;
            }
        }

        return new RetentionSweepResult( EntityType, totalDeleted, earliest, latest, categoryCounts );
    }

    /// <inheritdoc />
    public async Task<RetentionPreview> PreviewAgedRecordsAsync( int retentionDays, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<AuditEvent> eligible = db.AuditEvents
            .Where( e => e.TimestampUtc < cutoff );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( e => (DateTime?) e.TimestampUtc, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( e => (DateTime?) e.TimestampUtc, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
