using Microsoft.EntityFrameworkCore;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Core.Retention.Providers;

/// <summary>
/// Retention provider for dead-lettered notification deliveries.
/// Deletes <see cref="NotificationDelivery"/> records with terminal status
/// older than the configured retention period.
/// </summary>
public sealed class NotificationDeliveryRetentionProvider( WerkrDbContext db ) : IRetentionPolicyProvider {
    private static readonly DeliveryStatus[] s_terminalStatuses =
        [DeliveryStatus.Sent, DeliveryStatus.DeadLettered];

    /// <inheritdoc/>
    public string EntityType => "notification_delivery";

    /// <inheritdoc/>
    public async Task<RetentionSweepResult> DeleteAgedRecordsAsync( int retentionDays, int batchSize, CancellationToken ct ) {
        DateTime cutoff = DateTime.UtcNow.AddDays( -retentionDays );

        IQueryable<NotificationDelivery> eligible = db.NotificationDeliveries
            .Where( d => s_terminalStatuses.Contains( d.Status )
                      && d.CompletedUtc != null
                      && d.CompletedUtc < cutoff );

        DateTime? oldest = await eligible.MinAsync( d => d.CompletedUtc, ct );
        DateTime? newest = await eligible.MaxAsync( d => d.CompletedUtc, ct );

        int totalDeleted = 0;
        while (!ct.IsCancellationRequested) {
            int deleted = await db.NotificationDeliveries
                .Where( d => s_terminalStatuses.Contains( d.Status )
                          && d.CompletedUtc != null
                          && d.CompletedUtc < cutoff )
                .OrderBy( d => d.CompletedUtc )
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

        IQueryable<NotificationDelivery> eligible = db.NotificationDeliveries
            .Where( d => s_terminalStatuses.Contains( d.Status )
                      && d.CompletedUtc != null
                      && d.CompletedUtc < cutoff );

        int count = await eligible.CountAsync( ct );
        DateTime? oldest = count > 0 ? await eligible.MinAsync( d => d.CompletedUtc, ct ) : null;
        DateTime? newest = count > 0 ? await eligible.MaxAsync( d => d.CompletedUtc, ct ) : null;

        return new RetentionPreview( EntityType, count, oldest, newest );
    }
}
