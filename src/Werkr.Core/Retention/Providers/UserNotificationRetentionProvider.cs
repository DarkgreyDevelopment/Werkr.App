using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for user in-app notifications.
/// Deletes <see cref="UserNotification"/> records older than the configured retention period.
/// Uses the existing "notification" retention policy seed (90 days).
/// </summary>
public sealed class UserNotificationRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {

    /// <inheritdoc/>
    public string EntityType => "notification";

    /// <inheritdoc/>
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<UserNotification> eligible = db.UserNotifications
            .Where( n => n.CreatedUtc < cutoff );

        DateTime? oldest = await eligible.MinAsync( n => (DateTime?) n.CreatedUtc, ct );
        DateTime? newest = await eligible.MaxAsync( n => (DateTime?) n.CreatedUtc, ct );

        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.UserNotifications
                .Where( n => n.CreatedUtc < cutoff )
                .OrderBy( n => n.CreatedUtc )
                .Take( batchSize )
                .ExecuteDeleteAsync( ct );

            totalDeleted += deleted;
            if (deleted < batchSize) {
                break;
            }
        }

        return new RetentionSweepResult( EntityType, totalDeleted, oldest, newest );
    }

    /// <inheritdoc/>
    public async Task<RetentionPreview> PreviewAgedRecordsAsync( int retentionDays, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<UserNotification> eligible = db.UserNotifications
            .Where( n => n.CreatedUtc < cutoff );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( n => (DateTime?) n.CreatedUtc, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( n => (DateTime?) n.CreatedUtc, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
