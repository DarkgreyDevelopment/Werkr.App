using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Notifications;

namespace Werkr.Core.Notifications;

/// <summary>
/// Core notification delivery pipeline. Handles the full lifecycle:
/// event → subscription evaluation → template rendering → delivery attempt → retry/dead-letter.
/// </summary>
public sealed partial class NotificationDeliveryService(
    WerkrDbContext db,
    NotificationChannelResolver channelResolver,
    IAuditService auditService,
    ILogger<NotificationDeliveryService> logger
) : INotificationDeliveryService {

    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );

    /// <inheritdoc/>
    public async Task EmitAsync(
        string eventTypeId,
        string eventCategoryId,
        Dictionary<string, string> variables,
        long? workflowId = null,
        string[]? workflowTags = null,
        CancellationToken ct = default
    ) {
        try {
            // 1. Find matching subscriptions
            List<NotificationSubscription> subscriptions = await FindMatchingSubscriptionsAsync(
                eventCategoryId, workflowId, workflowTags, ct );

            if (subscriptions.Count == 0) {
                return;
            }

            // 2. Resolve templates for each channel type
            Dictionary<string, NotificationTemplate> templates = await db.NotificationTemplates
                .AsNoTracking( )
                .Where( t => t.EventTypeId == eventTypeId )
                .ToDictionaryAsync( t => t.ChannelType, t => t, StringComparer.OrdinalIgnoreCase, ct );

            // 3. Process each subscription
            foreach (NotificationSubscription subscription in subscriptions) {
                if (!subscription.IsEnabled) {
                    continue;
                }

                // Load channel
                NotificationChannel? channel = subscription.Channel
                    ?? await db.NotificationChannels.AsNoTracking( )
                        .FirstOrDefaultAsync( c => c.Id == subscription.ChannelId, ct );

                if (channel is null || !channel.IsEnabled) {
                    continue;
                }

                // Render template
                Dictionary<string, string> renderedVars = new( variables );
                if (templates.TryGetValue( channel.ChannelType, out NotificationTemplate? template )) {
                    if (template.Subject is not null) {
                        renderedVars["_renderedSubject"] = TemplateRenderer.Render( template.Subject, variables );
                        renderedVars["_renderedTitle"] = renderedVars["_renderedSubject"];
                    }
                    renderedVars["_renderedBody"] = TemplateRenderer.Render( template.Body, variables );
                }

                // Build delivery request
                NotificationDeliveryRequest deliveryRequest = new(
                    RecipientId: subscription.CreatedByUserId,
                    EventTypeId: eventTypeId,
                    EventCategoryId: eventCategoryId,
                    TemplateName: eventTypeId,
                    TemplateVariables: renderedVars,
                    ChannelConfig: new NotificationChannelConfig(
                        ChannelId: channel.Id,
                        ChannelType: channel.ChannelType,
                        ConfigurationJson: channel.Configuration
                    )
                );

                // Create delivery tracking record
                NotificationDelivery delivery = new( ) {
                    ChannelId = channel.Id,
                    EventTypeId = eventTypeId,
                    RecipientId = subscription.CreatedByUserId,
                    Status = DeliveryStatus.Pending,
                    AttemptCount = 0,
                    MaxAttempts = 3,
                    PayloadJson = JsonSerializer.Serialize( deliveryRequest, s_jsonOptions ),
                    CreatedUtc = DateTime.UtcNow,
                };

                _ = db.NotificationDeliveries.Add( delivery );
                _ = await db.SaveChangesAsync( ct );

                // Attempt delivery
                INotificationChannel? channelImpl = channelResolver.Resolve( channel.ChannelType );
                if (channelImpl is null) {
                    LogChannelNotFound( logger, channel.ChannelType );
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.ErrorMessage = $"No implementation for channel type '{channel.ChannelType}'.";
                    delivery.CompletedUtc = DateTime.UtcNow;
                    _ = await db.SaveChangesAsync( ct );
                    continue;
                }

                DeliveryResult result = await channelImpl.DeliverAsync( deliveryRequest, ct );
                delivery.AttemptCount = 1;
                delivery.LastAttemptUtc = result.TimestampUtc;

                if (result.Success) {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.CompletedUtc = result.TimestampUtc;

                    await auditService.LogAsync( new AuditEntry(
                        EventTypeId: AuditEventType.NotificationDeliverySent.ToEventId( ),
                        ActorId: "system",
                        ActorType: "System",
                        EntityType: "NotificationDelivery",
                        EntityId: delivery.Id.ToString( ),
                        ActionPerformed: "Sent",
                        Details: new { eventTypeId, channel.ChannelType, delivery.RecipientId }
                    ), ct );
                } else {
                    if (delivery.AttemptCount < delivery.MaxAttempts) {
                        delivery.Status = DeliveryStatus.Retrying;
                        delivery.NextRetryUtc = CalculateNextRetry( delivery.AttemptCount );
                        delivery.ErrorMessage = result.ErrorMessage;
                    } else {
                        delivery.Status = DeliveryStatus.DeadLettered;
                        delivery.ErrorMessage = result.ErrorMessage;
                        delivery.CompletedUtc = DateTime.UtcNow;

                        await auditService.LogAsync( new AuditEntry(
                            EventTypeId: AuditEventType.NotificationDeliveryDeadLettered.ToEventId( ),
                            ActorId: "system",
                            ActorType: "System",
                            EntityType: "NotificationDelivery",
                            EntityId: delivery.Id.ToString( ),
                            ActionPerformed: "DeadLettered",
                            Details: new { eventTypeId, channel.ChannelType, delivery.RecipientId, result.ErrorMessage }
                        ), ct );
                    }
                }

                _ = await db.SaveChangesAsync( ct );
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogDeliveryPipelineError( logger, eventTypeId, ex );
        }
    }

    private async Task<List<NotificationSubscription>> FindMatchingSubscriptionsAsync(
        string eventCategoryId,
        long? workflowId,
        string[]? workflowTags,
        CancellationToken ct
    ) {
        IQueryable<NotificationSubscription> query = db.NotificationSubscriptions
            .AsNoTracking( )
            .Include( s => s.Channel )
            .Where( s => s.EventCategoryId == eventCategoryId && s.IsEnabled );

        List<NotificationSubscription> results = [];

        // Global subscriptions
        List<NotificationSubscription> global = await query
            .Where( s => s.SubscriptionType == NotificationSubscriptionType.Global )
            .ToListAsync( ct );
        results.AddRange( global );

        // Per-workflow subscriptions
        if (workflowId.HasValue) {
            List<NotificationSubscription> perWorkflow = await query
                .Where( s => s.SubscriptionType == NotificationSubscriptionType.PerWorkflow
                          && s.WorkflowId == workflowId.Value )
                .ToListAsync( ct );
            results.AddRange( perWorkflow );
        }

        // Tag-based subscriptions
        if (workflowTags is { Length: > 0 }) {
            List<NotificationSubscription> tagBased = await query
                .Where( s => s.SubscriptionType == NotificationSubscriptionType.TagBased
                          && s.Tag != null
                          && workflowTags.Contains( s.Tag ) )
                .ToListAsync( ct );
            results.AddRange( tagBased );
        }

        return results;
    }

    /// <summary>
    /// Calculates the next retry time using exponential backoff (base 30 seconds).
    /// </summary>
    public static DateTime CalculateNextRetry( int attemptCount ) {
        int delaySeconds = 30 * (1 << (attemptCount - 1)); // 30s, 60s, 120s, 240s...
        return DateTime.UtcNow.AddSeconds( delaySeconds );
    }

    [LoggerMessage( Level = LogLevel.Warning, Message = "No channel implementation found for type '{ChannelType}'" )]
    private static partial void LogChannelNotFound( ILogger logger, string channelType );

    [LoggerMessage( Level = LogLevel.Error, Message = "Error in notification delivery pipeline for {EventTypeId}" )]
    private static partial void LogDeliveryPipelineError( ILogger logger, string eventTypeId, Exception ex );
}
