namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// Status of a notification delivery attempt.
/// </summary>
public enum DeliveryStatus {
    /// <summary>Delivery is pending initial attempt.</summary>
    Pending = 0,

    /// <summary>Delivery succeeded.</summary>
    Sent = 1,

    /// <summary>Delivery failed on latest attempt.</summary>
    Failed = 2,

    /// <summary>Delivery failed but will be retried.</summary>
    Retrying = 3,

    /// <summary>Delivery exhausted all retry attempts and is permanently failed.</summary>
    DeadLettered = 4,
}
