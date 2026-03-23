using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Werkr.Core.Notifications;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Api.Services;

/// <summary>
/// Background service that processes the notification retry queue.
/// Runs on a configurable interval (default: 30 seconds).
/// Queries deliveries with Status=Retrying and NextRetryUtc &lt;= UtcNow,
/// attempts redelivery, and updates status accordingly.
/// </summary>
public sealed partial class NotificationRetryService(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationRetryService> logger
) : BackgroundService {

    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );
    private readonly TimeSpan _interval = TimeSpan.FromSeconds( 30 );

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogServiceStarted( logger );

        while (!stoppingToken.IsCancellationRequested) {
            await Task.Delay( _interval, stoppingToken );

            try {
                await ProcessRetryQueueAsync( stoppingToken );
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                LogProcessingError( logger, ex );
            }
        }
    }

    private async Task ProcessRetryQueueAsync( CancellationToken ct ) {
        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );
        NotificationChannelResolver channelResolver = scope.ServiceProvider.GetRequiredService<NotificationChannelResolver>( );

        DateTime now = DateTime.UtcNow;
        List<NotificationDelivery> retryable = await db.NotificationDeliveries
            .Include( d => d.Channel )
            .Where( d => d.Status == DeliveryStatus.Retrying && d.NextRetryUtc != null && d.NextRetryUtc <= now )
            .OrderBy( d => d.NextRetryUtc )
            .Take( 50 ) // Process in batches
            .ToListAsync( ct );

        if (retryable.Count == 0) {
            return;
        }

        LogRetryBatch( logger, retryable.Count );

        foreach (NotificationDelivery delivery in retryable) {
            if (delivery.Channel is null || !delivery.Channel.IsEnabled) {
                delivery.Status = DeliveryStatus.DeadLettered;
                delivery.ErrorMessage = "Channel disabled or deleted.";
                delivery.CompletedUtc = DateTime.UtcNow;
                continue;
            }

            INotificationChannel? channelImpl = channelResolver.Resolve( delivery.Channel.ChannelType );
            if (channelImpl is null) {
                delivery.Status = DeliveryStatus.DeadLettered;
                delivery.ErrorMessage = $"No implementation for channel type '{delivery.Channel.ChannelType}'.";
                delivery.CompletedUtc = DateTime.UtcNow;
                continue;
            }

            NotificationDeliveryRequest? request = JsonSerializer.Deserialize<NotificationDeliveryRequest>(
                delivery.PayloadJson, s_jsonOptions );

            if (request is null) {
                delivery.Status = DeliveryStatus.DeadLettered;
                delivery.ErrorMessage = "Failed to deserialize delivery payload.";
                delivery.CompletedUtc = DateTime.UtcNow;
                continue;
            }

            DeliveryResult result = await channelImpl.DeliverAsync( request, ct );
            delivery.AttemptCount++;
            delivery.LastAttemptUtc = result.TimestampUtc;

            if (result.Success) {
                delivery.Status = DeliveryStatus.Sent;
                delivery.CompletedUtc = result.TimestampUtc;
            } else if (delivery.AttemptCount >= delivery.MaxAttempts) {
                delivery.Status = DeliveryStatus.DeadLettered;
                delivery.ErrorMessage = result.ErrorMessage;
                delivery.CompletedUtc = DateTime.UtcNow;
            } else {
                delivery.Status = DeliveryStatus.Retrying;
                delivery.NextRetryUtc = NotificationDeliveryService.CalculateNextRetry( delivery.AttemptCount );
                delivery.ErrorMessage = result.ErrorMessage;
            }
        }

        _ = await db.SaveChangesAsync( ct );
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "NotificationRetryService started" )]
    private static partial void LogServiceStarted( ILogger logger );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Processing {Count} retryable notification deliveries" )]
    private static partial void LogRetryBatch( ILogger logger, int count );

    [LoggerMessage( Level = LogLevel.Error, Message = "Error processing notification retry queue" )]
    private static partial void LogProcessingError( ILogger logger, Exception ex );
}
