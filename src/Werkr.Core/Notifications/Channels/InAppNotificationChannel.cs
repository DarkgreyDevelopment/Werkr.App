using Microsoft.Extensions.Logging;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Core.Notifications.Channels;

/// <summary>
/// In-app notification channel — persists notifications to the database
/// and pushes real-time updates via SignalR when the user is connected.
/// </summary>
public sealed partial class InAppNotificationChannel(
    WerkrDbContext db,
    INotificationHubService hubService,
    ILogger<InAppNotificationChannel> logger
) : INotificationChannel {

    /// <inheritdoc/>
    public string ChannelType => "inapp";

    /// <inheritdoc/>
    public async Task<DeliveryResult> DeliverAsync( NotificationDeliveryRequest request, CancellationToken ct ) {
        try {
            string title = request.TemplateVariables.TryGetValue( "_renderedTitle", out string? t )
                ? t
                : request.EventTypeId;

            string body = request.TemplateVariables.TryGetValue( "_renderedBody", out string? b )
                ? b
                : $"Notification for {request.EventTypeId}";

            string? link = request.TemplateVariables.TryGetValue( "link", out string? l ) ? l : null;

            // Persist to database
            UserNotification notification = new( ) {
                UserId = request.RecipientId,
                EventTypeId = request.EventTypeId,
                EventCategoryId = request.EventCategoryId,
                Title = title,
                Body = body,
                IsRead = false,
                CreatedUtc = DateTime.UtcNow,
                Link = link,
            };

            _ = db.UserNotifications.Add( notification );
            _ = await db.SaveChangesAsync( ct );

            // Push via SignalR if user is connected
            await hubService.SendToUserAsync(
                request.RecipientId,
                notification.Id,
                title,
                body,
                request.EventTypeId,
                link,
                ct
            );

            LogInAppDelivered( logger, request.RecipientId, request.EventTypeId );
            return new DeliveryResult( true, null, DateTime.UtcNow );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogInAppFailed( logger, request.RecipientId, ex );
            return new DeliveryResult( false, ex.Message, DateTime.UtcNow );
        }
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "In-app notification delivered to {UserId} for {EventType}" )]
    private static partial void LogInAppDelivered( ILogger logger, string userId, string eventType );

    [LoggerMessage( Level = LogLevel.Error, Message = "In-app notification delivery failed for {UserId}" )]
    private static partial void LogInAppFailed( ILogger logger, string userId, Exception ex );
}
