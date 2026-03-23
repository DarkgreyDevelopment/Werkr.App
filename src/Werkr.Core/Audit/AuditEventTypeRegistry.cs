using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Werkr.Common.Models.Audit;

namespace Werkr.Core.Audit;

/// <summary>
/// Singleton, ConcurrentDictionary-backed registry for audit event type definitions.
/// Validates event type IDs: lowercase alphanumeric + dots + underscores, max 128 chars.
/// </summary>
public sealed partial class AuditEventTypeRegistry : IAuditEventTypeRegistry {
    private readonly ConcurrentDictionary<string, AuditEventTypeDto> _types = new( StringComparer.OrdinalIgnoreCase );

    /// <inheritdoc/>
    public void Register( string eventTypeId, string displayName, string category, string sourceModule ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( eventTypeId );
        ArgumentException.ThrowIfNullOrWhiteSpace( displayName );
        ArgumentException.ThrowIfNullOrWhiteSpace( category );
        ArgumentException.ThrowIfNullOrWhiteSpace( sourceModule );

        if (eventTypeId.Length > 128) {
            throw new ArgumentException( $"Event type ID must be 128 characters or fewer: '{eventTypeId}'.", nameof( eventTypeId ) );
        }

        if (!EventTypeIdPattern( ).IsMatch( eventTypeId )) {
            throw new ArgumentException( $"Event type ID must be lowercase alphanumeric with dots and underscores: '{eventTypeId}'.", nameof( eventTypeId ) );
        }

        _ = _types.TryAdd( eventTypeId, new AuditEventTypeDto( eventTypeId, displayName, category, sourceModule ) );
    }

    /// <inheritdoc/>
    public IReadOnlyList<AuditEventTypeDto> GetAll( ) =>
        [.. _types.Values.OrderBy( t => t.Category ).ThenBy( t => t.EventTypeId )];

    /// <inheritdoc/>
    public AuditEventTypeDto? GetByTypeId( string eventTypeId ) =>
        _types.TryGetValue( eventTypeId, out AuditEventTypeDto? dto ) ? dto : null;

    /// <inheritdoc/>
    public IReadOnlyList<string> GetCategories( ) =>
        [.. _types.Values.Select( t => t.Category ).Distinct( StringComparer.OrdinalIgnoreCase ).OrderBy( c => c )];

    /// <inheritdoc/>
    public IReadOnlyList<string> GetModules( ) =>
        [.. _types.Values.Select( t => t.SourceModule ).Distinct( StringComparer.OrdinalIgnoreCase ).OrderBy( m => m )];

    [GeneratedRegex( @"^[a-z0-9][a-z0-9._]*$" )]
    private static partial Regex EventTypeIdPattern( );
}
