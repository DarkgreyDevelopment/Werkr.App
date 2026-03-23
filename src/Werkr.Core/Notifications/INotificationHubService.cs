namespace Werkr.Core.Notifications;

/// <summary>
/// Abstraction for pushing real-time notifications to connected users via SignalR.
/// The implementation lives in the Server project; the API project uses a no-op stub.
/// This keeps <c>Werkr.Core</c> free of SignalR dependencies.
/// </summary>
public interface INotificationHubService {
    /// <summary>
    /// Sends a notification to a specific user. If the user is connected,
    /// they receive it in real-time; otherwise it's persisted for next login.
    /// </summary>
    /// <param name="userId">Target user ID.</param>
    /// <param name="notificationId">Database ID of the persisted notification.</param>
    /// <param name="title">Notification title.</param>
    /// <param name="body">Notification body.</param>
    /// <param name="eventTypeId">Event type identifier.</param>
    /// <param name="link">Optional deep link URL.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendToUserAsync( string userId, long notificationId, string title, string body,
        string eventTypeId, string? link, CancellationToken ct );
}

/// <summary>
/// No-op implementation of <see cref="INotificationHubService"/> for use in the API project.
/// In-app notifications are persisted to the database; real-time push is handled by the Server.
/// </summary>
public sealed class NullNotificationHubService : INotificationHubService {
    /// <inheritdoc/>
    public Task SendToUserAsync( string userId, long notificationId, string title, string body,
        string eventTypeId, string? link, CancellationToken ct ) =>
        Task.CompletedTask;
}
