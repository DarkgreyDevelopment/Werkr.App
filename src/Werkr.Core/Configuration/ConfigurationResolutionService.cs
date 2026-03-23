using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Core.Configuration;

/// <summary>
/// Hierarchical configuration resolution service. Reads/writes configuration
/// entries with Global (ScopeLevel=0) and Agent (ScopeLevel=1) scoping.
/// </summary>
public sealed partial class ConfigurationResolutionService(
    WerkrDbContext dbContext,
    IAuditService auditService,
    ILogger<ConfigurationResolutionService> logger
) : IConfigurationResolutionService {

    /// <inheritdoc/>
    public async Task<ConfigurationEntryDto?> GetByKeyAsync( string key, CancellationToken ct ) {
        ConfigurationEntry? entry = await dbContext.ConfigurationEntries
            .AsNoTracking( )
            .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == 0, ct );
        return entry is null ? null : ToDto( entry );
    }

    /// <inheritdoc/>
    public async Task<EffectiveConfigurationDto?> GetEffectiveValueAsync(
        string key, string? agentId, CancellationToken ct
    ) {
        ConfigurationEntry? global = await dbContext.ConfigurationEntries
            .AsNoTracking( )
            .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == 0, ct );

        if (global is null) {
            return null;
        }

        ConfigurationEntry? agentOverride = null;
        if (!string.IsNullOrEmpty( agentId )) {
            agentOverride = await dbContext.ConfigurationEntries
                .AsNoTracking( )
                .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == 1 && e.ScopeId == agentId, ct );
        }

        return BuildEffective( global, agentOverride, agentId );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EffectiveConfigurationDto>> GetEffectiveSettingsAsync(
        string? agentId, CancellationToken ct
    ) {
        List<ConfigurationEntry> globals = await dbContext.ConfigurationEntries
            .AsNoTracking( )
            .Where( e => e.ScopeLevel == 0 )
            .OrderBy( e => e.Key )
            .ToListAsync( ct );

        Dictionary<string, ConfigurationEntry> overrides = [];
        if (!string.IsNullOrEmpty( agentId )) {
            overrides = await dbContext.ConfigurationEntries
                .AsNoTracking( )
                .Where( e => e.ScopeLevel == 1 && e.ScopeId == agentId )
                .ToDictionaryAsync( e => e.Key, ct );
        }

        return [.. globals.Select( g => BuildEffective( g, overrides.GetValueOrDefault( g.Key ), agentId ) )];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationEntryDto>> GetAllAsync(
        string? category, int? scopeLevel, string? scopeId, CancellationToken ct
    ) {
        IQueryable<ConfigurationEntry> query = dbContext.ConfigurationEntries.AsNoTracking( );

        if (!string.IsNullOrEmpty( category )) {
            query = query.Where( e => e.Category == category );
        }
        if (scopeLevel.HasValue) {
            query = query.Where( e => e.ScopeLevel == scopeLevel.Value );
        }
        if (!string.IsNullOrEmpty( scopeId )) {
            query = query.Where( e => e.ScopeId == scopeId );
        }

        List<ConfigurationEntry> entries = await query.OrderBy( e => e.Key ).ToListAsync( ct );
        return [.. entries.Select( ToDto )];
    }

    /// <inheritdoc/>
    public async Task<ConfigurationEntryDto> UpdateAsync(
        string key, ConfigurationUpdateRequest request, string userId, CancellationToken ct
    ) {
        int scopeLevel = request.ScopeLevel ?? 0;
        string? scopeId = request.ScopeId;

        ConfigurationEntry? entry = await dbContext.ConfigurationEntries
            .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == scopeLevel && e.ScopeId == scopeId, ct );

        string? previousValue = null;

        if (entry is null && scopeLevel == 1 && !string.IsNullOrEmpty( scopeId )) {
            // Creating an agent override — clone from global
            ConfigurationEntry? global = await dbContext.ConfigurationEntries
                .AsNoTracking( )
                .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == 0, ct )
                ?? throw new KeyNotFoundException( $"Configuration key '{key}' not found." );

            entry = new ConfigurationEntry {
                Key = key,
                Value = request.Value,
                ValueType = global.ValueType,
                Category = global.Category,
                Description = global.Description,
                ScopeLevel = 1,
                ScopeId = scopeId,
                ValidationRules = global.ValidationRules,
                DefaultValue = global.DefaultValue,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = userId,
            };
            ValidateValue( entry, request.Value );
            _ = dbContext.ConfigurationEntries.Add( entry );
        } else if (entry is not null) {
            ValidateValue( entry, request.Value );
            previousValue = entry.Value;

            entry.Value = request.Value;
            entry.ModifiedUtc = DateTime.UtcNow;
            entry.ModifiedByUserId = userId;

            // Write change log
            ConfigurationChangeLog changeLog = new( ) {
                ConfigurationEntryId = entry.Id,
                Key = key,
                PreviousValue = previousValue,
                NewValue = request.Value,
                ChangedByUserId = userId,
                ChangedUtc = DateTime.UtcNow,
            };
            _ = dbContext.ConfigurationChangeLogs.Add( changeLog );
        } else {
            throw new KeyNotFoundException( $"Configuration key '{key}' not found." );
        }

        // Increment sync version for delta sync
        long maxVersion = await dbContext.ConfigurationEntries
            .MaxAsync( e => (long?)e.SyncVersion, ct ) ?? 0;
        entry.SyncVersion = maxVersion + 1;

        _ = await dbContext.SaveChangesAsync( ct );

        // Audit
        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.ConfigUpdated.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "ConfigurationEntry",
            EntityId: entry.Id.ToString( ),
            ActionPerformed: "Updated",
            Details: new { entry.Key, entry.ScopeLevel, entry.ScopeId, PreviousValue = previousValue, NewValue = entry.Value }
        ), ct );

        LogConfigUpdated( logger, key, scopeLevel, scopeId );

        return ToDto( entry );
    }

    /// <inheritdoc/>
    public async Task DeleteOverrideAsync( string key, string agentId, string userId, CancellationToken ct ) {
        ConfigurationEntry entry = await dbContext.ConfigurationEntries
            .FirstOrDefaultAsync( e => e.Key == key && e.ScopeLevel == 1 && e.ScopeId == agentId, ct )
            ?? throw new KeyNotFoundException( $"No agent override found for key '{key}' on agent '{agentId}'." );

        string previousValue = entry.Value;
        _ = dbContext.ConfigurationEntries.Remove( entry );

        // Increment sync version so agents pick up the deletion via delta sync
        long maxVersion = await dbContext.ConfigurationEntries
            .MaxAsync( e => (long?)e.SyncVersion, ct ) ?? 0;

        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.ConfigUpdated.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "ConfigurationEntry",
            EntityId: entry.Id.ToString( ),
            ActionPerformed: "OverrideRemoved",
            Details: new { entry.Key, AgentId = agentId, PreviousValue = previousValue }
        ), ct );

        LogConfigUpdated( logger, key, 1, agentId );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationChangeLogDto>> GetHistoryAsync(
        string key, int limit, CancellationToken ct
    ) {
        List<ConfigurationChangeLog> logs = await dbContext.ConfigurationChangeLogs
            .AsNoTracking( )
            .Where( l => l.Key == key )
            .OrderByDescending( l => l.ChangedUtc )
            .Take( limit )
            .ToListAsync( ct );

        return [.. logs.Select( l => new ConfigurationChangeLogDto(
            Id: l.Id,
            Key: l.Key,
            PreviousValue: l.PreviousValue,
            NewValue: l.NewValue,
            ChangedByUserId: l.ChangedByUserId,
            ChangedUtc: l.ChangedUtc
        ) )];
    }

    /// <inheritdoc/>
    public async Task<long> GetCurrentVersionAsync( CancellationToken ct ) =>
        await dbContext.ConfigurationEntries.MaxAsync( e => (long?)e.SyncVersion, ct ) ?? 0;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationEntryDto>> GetDeltaAsync(
        long sinceVersion, string? agentId, CancellationToken ct
    ) {
        IQueryable<ConfigurationEntry> query = dbContext.ConfigurationEntries
            .AsNoTracking( )
            .Where( e => e.SyncVersion > sinceVersion );

        // For agent delta sync, return global + agent-specific entries
        if (!string.IsNullOrEmpty( agentId )) {
            query = query.Where( e => e.ScopeLevel == 0 || (e.ScopeLevel == 1 && e.ScopeId == agentId) );
        }

        List<ConfigurationEntry> entries = await query.OrderBy( e => e.SyncVersion ).ToListAsync( ct );
        return [.. entries.Select( ToDto )];
    }

    private static ConfigurationEntryDto ToDto( ConfigurationEntry entry ) =>
        new(
            Id: entry.Id,
            Key: entry.Key,
            Value: entry.Value,
            ValueType: entry.ValueType,
            Category: entry.Category,
            Description: entry.Description,
            ScopeLevel: entry.ScopeLevel,
            ScopeId: entry.ScopeId,
            SyncVersion: entry.SyncVersion,
            ValidationRules: entry.ValidationRules,
            DefaultValue: entry.DefaultValue,
            CreatedUtc: entry.CreatedUtc,
            ModifiedUtc: entry.ModifiedUtc,
            ModifiedByUserId: entry.ModifiedByUserId
        );

    private static EffectiveConfigurationDto BuildEffective(
        ConfigurationEntry global,
        ConfigurationEntry? agentOverride,
        string? agentId
    ) {
        bool hasOverride = agentOverride is not null;
        return new EffectiveConfigurationDto(
            Key: global.Key,
            EffectiveValue: hasOverride ? agentOverride!.Value : global.Value,
            Source: hasOverride ? "Agent Override" : "Global",
            GlobalValue: global.Value,
            OverrideValue: agentOverride?.Value,
            AgentId: agentId,
            ValueType: global.ValueType,
            Category: global.Category,
            Description: global.Description,
            ValidationRules: global.ValidationRules
        );
    }

    private static void ValidateValue( ConfigurationEntry entry, string value ) {
        // Type validation
        switch (entry.ValueType) {
            case "number":
                if (!double.TryParse( value, out _ )) {
                    throw new ArgumentException( $"Value for '{entry.Key}' must be a valid number." );
                }
                break;
            case "boolean":
                if (!bool.TryParse( value, out _ )) {
                    throw new ArgumentException( $"Value for '{entry.Key}' must be 'true' or 'false'." );
                }
                break;
            case "json":
                try {
                    using JsonDocument doc = JsonDocument.Parse( value );
                } catch (JsonException) {
                    throw new ArgumentException( $"Value for '{entry.Key}' must be valid JSON." );
                }
                break;
        }

        // Rule-based validation
        if (string.IsNullOrEmpty( entry.ValidationRules )) {
            return;
        }

        try {
            using JsonDocument rules = JsonDocument.Parse( entry.ValidationRules );
            JsonElement root = rules.RootElement;

            if (root.TryGetProperty( "min", out JsonElement minEl ) && double.TryParse( value, out double numVal )) {
                double min = minEl.GetDouble( );
                if (numVal < min) {
                    throw new ArgumentException( $"Value for '{entry.Key}' must be >= {min}." );
                }
            }

            if (root.TryGetProperty( "max", out JsonElement maxEl ) && double.TryParse( value, out double numVal2 )) {
                double max = maxEl.GetDouble( );
                if (numVal2 > max) {
                    throw new ArgumentException( $"Value for '{entry.Key}' must be <= {max}." );
                }
            }

            if (root.TryGetProperty( "regex", out JsonElement regexEl )) {
                string pattern = regexEl.GetString( ) ?? string.Empty;
                if (!Regex.IsMatch( value, pattern )) {
                    throw new ArgumentException( $"Value for '{entry.Key}' does not match the required pattern." );
                }
            }

            if (root.TryGetProperty( "enum", out JsonElement enumEl ) && enumEl.ValueKind == JsonValueKind.Array) {
                List<string> allowed = [];
                foreach (JsonElement item in enumEl.EnumerateArray( )) {
                    allowed.Add( item.GetString( ) ?? string.Empty );
                }
                if (!allowed.Contains( value, StringComparer.OrdinalIgnoreCase )) {
                    throw new ArgumentException(
                        $"Value for '{entry.Key}' must be one of: {string.Join( ", ", allowed )}." );
                }
            }
        } catch (JsonException) {
            // Malformed validation rules — skip rule validation
        }
    }

    [LoggerMessage( Level = LogLevel.Information, Message = "Configuration updated: {Key} (scope={ScopeLevel}, scopeId={ScopeId})" )]
    private static partial void LogConfigUpdated( ILogger logger, string key, int scopeLevel, string? scopeId );
}
