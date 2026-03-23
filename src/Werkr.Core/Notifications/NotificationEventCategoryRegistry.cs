using System.Collections.Concurrent;

namespace Werkr.Core.Notifications;

/// <summary>
/// Read/write registry for notification event categories and their event types.
/// Categories are registered at application startup and queried at runtime.
/// </summary>
public interface INotificationEventCategoryRegistry {
    /// <summary>Registers an event category with its event types.</summary>
    void Register( NotificationEventCategory category );

    /// <summary>Returns all registered categories ordered by CategoryId.</summary>
    IReadOnlyList<NotificationEventCategory> GetAll( );

    /// <summary>Returns a category by ID, or null if not registered.</summary>
    NotificationEventCategory? GetById( string categoryId );
}

/// <summary>
/// Singleton, ConcurrentDictionary-backed registry for notification event categories.
/// </summary>
public sealed class NotificationEventCategoryRegistry : INotificationEventCategoryRegistry {
    private readonly ConcurrentDictionary<string, NotificationEventCategory> _categories = new( StringComparer.OrdinalIgnoreCase );

    /// <inheritdoc/>
    public void Register( NotificationEventCategory category ) {
        ArgumentNullException.ThrowIfNull( category );
        ArgumentException.ThrowIfNullOrWhiteSpace( category.CategoryId );
        ArgumentException.ThrowIfNullOrWhiteSpace( category.DisplayName );

        if (category.EventTypes.Count == 0) {
            throw new ArgumentException( $"Category '{category.CategoryId}' must have at least one event type.", nameof( category ) );
        }

        _ = _categories.TryAdd( category.CategoryId, category );
    }

    /// <inheritdoc/>
    public IReadOnlyList<NotificationEventCategory> GetAll( ) =>
        [.. _categories.Values.OrderBy( c => c.CategoryId )];

    /// <inheritdoc/>
    public NotificationEventCategory? GetById( string categoryId ) =>
        _categories.TryGetValue( categoryId, out NotificationEventCategory? category ) ? category : null;
}

/// <summary>
/// A notification event category with its constituent event types.
/// </summary>
/// <param name="CategoryId">Unique category identifier, e.g. "workflow_execution".</param>
/// <param name="DisplayName">Human-readable display name, e.g. "Workflow Execution".</param>
/// <param name="EventTypes">Event types within this category.</param>
/// <param name="DefaultSubscribed">Whether new users are subscribed by default.</param>
public record NotificationEventCategory(
    string CategoryId,
    string DisplayName,
    IReadOnlyList<NotificationEventType> EventTypes,
    bool DefaultSubscribed
);

/// <summary>
/// A specific event type within a notification category.
/// </summary>
/// <param name="EventTypeId">Unique event type identifier, e.g. "workflow.run.failed".</param>
/// <param name="DisplayName">Human-readable display name, e.g. "Workflow Run Failed".</param>
public record NotificationEventType(
    string EventTypeId,
    string DisplayName
);
