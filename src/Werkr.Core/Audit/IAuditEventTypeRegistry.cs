using Werkr.Common.Models.Audit;

namespace Werkr.Core.Audit;

/// <summary>
/// Thread-safe registry for audit event type definitions.
/// New event types can be registered at startup without schema changes.
/// </summary>
public interface IAuditEventTypeRegistry {
    /// <summary>Registers an event type definition. Duplicate registrations are ignored.</summary>
    void Register( string eventTypeId, string displayName, string category, string sourceModule );

    /// <summary>Returns all registered event type definitions.</summary>
    IReadOnlyList<AuditEventTypeDto> GetAll( );

    /// <summary>Returns the definition for a specific event type ID, or null if not registered.</summary>
    AuditEventTypeDto? GetByTypeId( string eventTypeId );

    /// <summary>Returns all distinct categories, sorted alphabetically.</summary>
    IReadOnlyList<string> GetCategories( );

    /// <summary>Returns all distinct source modules, sorted alphabetically.</summary>
    IReadOnlyList<string> GetModules( );
}
