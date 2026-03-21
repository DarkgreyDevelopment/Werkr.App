using Werkr.Common.Models;

namespace Werkr.Core.Configuration;

/// <summary>
/// Service for reading and updating hierarchical configuration entries.
/// Resolves effective values by merging Global and Agent-scoped overrides.
/// </summary>
public interface IConfigurationResolutionService {

    /// <summary>Returns a single configuration entry by key (Global scope).</summary>
    Task<ConfigurationEntryDto?> GetByKeyAsync( string key, CancellationToken ct );

    /// <summary>
    /// Returns the effective value for a key, resolving the scope chain:
    /// Agent override (if <paramref name="agentId"/> provided and override exists) → Global.
    /// </summary>
    Task<EffectiveConfigurationDto?> GetEffectiveValueAsync( string key, string? agentId, CancellationToken ct );

    /// <summary>Returns all effective settings for an agent (merged global + overrides).</summary>
    Task<IReadOnlyList<EffectiveConfigurationDto>> GetEffectiveSettingsAsync( string? agentId, CancellationToken ct );

    /// <summary>Lists configuration entries with optional filtering.</summary>
    Task<IReadOnlyList<ConfigurationEntryDto>> GetAllAsync(
        string? category, int? scopeLevel, string? scopeId, CancellationToken ct );

    /// <summary>
    /// Updates a configuration entry value. Creates a change log, increments
    /// the sync version, and fires an audit event.
    /// </summary>
    Task<ConfigurationEntryDto> UpdateAsync(
        string key, ConfigurationUpdateRequest request, string userId, CancellationToken ct );

    /// <summary>Returns the change history for a configuration key.</summary>
    Task<IReadOnlyList<ConfigurationChangeLogDto>> GetHistoryAsync( string key, int limit, CancellationToken ct );

    /// <summary>Returns the current maximum sync version (for delta sync baseline).</summary>
    Task<long> GetCurrentVersionAsync( CancellationToken ct );

    /// <summary>
    /// Returns all entries with <c>SyncVersion > sinceVersion</c>,
    /// with agent-effective values resolved.
    /// </summary>
    Task<IReadOnlyList<ConfigurationEntryDto>> GetDeltaAsync(
        long sinceVersion, string? agentId, CancellationToken ct );
}
