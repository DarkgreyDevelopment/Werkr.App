namespace Werkr.Core.Notifications;

/// <summary>
/// Common delivery interface for all notification channels.
/// Each channel type (email, webhook, in-app) implements this interface.
/// </summary>
public interface INotificationChannel {
    /// <summary>Channel type identifier: "email", "webhook", "inapp".</summary>
    string ChannelType { get; }

    /// <summary>Deliver a single notification. Returns delivery result with status and error details.</summary>
    Task<DeliveryResult> DeliverAsync( NotificationDeliveryRequest request, CancellationToken ct );
}

/// <summary>
/// A request to deliver a notification through a specific channel.
/// The delivery interface is standalone — the subscription model is one routing layer
/// that produces delivery requests, but other system components may produce delivery requests directly.
/// </summary>
/// <param name="RecipientId">User ID for in-app/email; endpoint for webhook.</param>
/// <param name="EventTypeId">Event type identifier, e.g. "workflow.run.failed".</param>
/// <param name="EventCategoryId">Event category identifier, e.g. "workflow_execution".</param>
/// <param name="TemplateName">Template to render (channel+event specific).</param>
/// <param name="TemplateVariables">Variable values for template interpolation.</param>
/// <param name="ChannelConfig">Channel-specific configuration (SMTP settings, webhook URL, etc.).</param>
public record NotificationDeliveryRequest(
    string RecipientId,
    string EventTypeId,
    string EventCategoryId,
    string TemplateName,
    Dictionary<string, string> TemplateVariables,
    NotificationChannelConfig ChannelConfig
);

/// <summary>
/// Channel configuration resolved from the database and passed to the channel implementation.
/// </summary>
/// <param name="ChannelId">Database ID of the configured channel.</param>
/// <param name="ChannelType">Channel type identifier.</param>
/// <param name="ConfigurationJson">Decrypted channel-specific configuration JSON.</param>
public record NotificationChannelConfig(
    long ChannelId,
    string ChannelType,
    string ConfigurationJson
);

/// <summary>
/// Result of a delivery attempt.
/// </summary>
/// <param name="Success">Whether the delivery succeeded.</param>
/// <param name="ErrorMessage">Error details if delivery failed.</param>
/// <param name="TimestampUtc">UTC timestamp of the delivery attempt.</param>
public record DeliveryResult(
    bool Success,
    string? ErrorMessage,
    DateTime TimestampUtc
);
