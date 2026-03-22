using System.Collections.Frozen;

namespace Werkr.Core.Retention;

/// <summary>
/// Thread-safe registry mapping entity type strings to their
/// <see cref="IRetentionPolicyProvider"/> implementations.
/// Built at startup and then frozen for lock-free reads.
/// </summary>
public sealed class RetentionPolicyRegistry {

    private readonly Dictionary<string, IRetentionPolicyProvider> _providers = new( StringComparer.OrdinalIgnoreCase );
    private FrozenDictionary<string, IRetentionPolicyProvider>? _frozen;

    /// <summary>
    /// Registers a provider for the given entity type.
    /// Must be called during startup before any reads.
    /// </summary>
    /// <param name="provider">The provider to register.</param>
    /// <exception cref="ArgumentException">Thrown if a provider for the same entity type is already registered.</exception>
    public void Register( IRetentionPolicyProvider provider ) {
        ArgumentNullException.ThrowIfNull( provider );

        if (!_providers.TryAdd( provider.EntityType, provider )) {
            throw new ArgumentException(
                $"A retention provider for entity type '{provider.EntityType}' is already registered.",
                nameof( provider ) );
        }

        // Invalidate frozen snapshot so next read rebuilds it
        _frozen = null;
    }

    /// <summary>
    /// Returns the provider for the given entity type, or null if not registered.
    /// </summary>
    /// <param name="entityType">The entity type key.</param>
    /// <returns>The matching provider, or null.</returns>
    public IRetentionPolicyProvider? GetProvider( string entityType ) {
        FrozenDictionary<string, IRetentionPolicyProvider> frozen = EnsureFrozen( );
        return frozen.TryGetValue( entityType, out IRetentionPolicyProvider? provider ) ? provider : null;
    }

    /// <summary>
    /// Returns whether a provider is registered for the given entity type.
    /// </summary>
    public bool HasProvider( string entityType ) {
        FrozenDictionary<string, IRetentionPolicyProvider> frozen = EnsureFrozen( );
        return frozen.ContainsKey( entityType );
    }

    /// <summary>
    /// Returns all registered providers.
    /// </summary>
    public IReadOnlyCollection<IRetentionPolicyProvider> GetAll( ) {
        FrozenDictionary<string, IRetentionPolicyProvider> frozen = EnsureFrozen( );
        return frozen.Values;
    }

    private FrozenDictionary<string, IRetentionPolicyProvider> EnsureFrozen( ) {
        return _frozen ??= _providers.ToFrozenDictionary( StringComparer.OrdinalIgnoreCase );
    }
}
