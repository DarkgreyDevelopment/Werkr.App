namespace Werkr.Core.Notifications;

/// <summary>
/// Resolves an <see cref="INotificationChannel"/> implementation by channel type string.
/// Takes all registered channel implementations via DI multi-registration.
/// </summary>
public sealed class NotificationChannelResolver {
    private readonly Dictionary<string, INotificationChannel> _channels;

    /// <summary>Creates a resolver from all registered channel implementations.</summary>
    public NotificationChannelResolver( IEnumerable<INotificationChannel> channels ) {
        _channels = channels.ToDictionary( c => c.ChannelType, c => c, StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>Returns the channel implementation for the given type, or null if not registered.</summary>
    public INotificationChannel? Resolve( string channelType ) =>
        _channels.TryGetValue( channelType, out INotificationChannel? channel ) ? channel : null;

    /// <summary>Returns all registered channel type identifiers.</summary>
    public IReadOnlyList<string> GetRegisteredTypes( ) => [.. _channels.Keys];
}
