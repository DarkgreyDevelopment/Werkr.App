namespace Werkr.Data.Entities.Notifications;

/// <summary>
/// The scope of a notification subscription.
/// </summary>
public enum NotificationSubscriptionType {
    /// <summary>Subscribe to events from a specific workflow.</summary>
    PerWorkflow = 0,

    /// <summary>Subscribe to events from all workflows matching a tag.</summary>
    TagBased = 1,

    /// <summary>Subscribe to all events in a category, regardless of workflow.</summary>
    Global = 2,
}
