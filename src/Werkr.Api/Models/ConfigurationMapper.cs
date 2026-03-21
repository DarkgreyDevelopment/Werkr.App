using Werkr.Common.Models;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Api.Models;

/// <summary>
/// Maps between ConfigurationEntry/ConfigurationChangeLog entities and DTOs.
/// </summary>
internal static class ConfigurationMapper {

    /// <summary>Maps a <see cref="ConfigurationEntry"/> entity to a <see cref="ConfigurationEntryDto"/>.</summary>
    public static ConfigurationEntryDto ToDto( ConfigurationEntry entry ) =>
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

    /// <summary>Maps a <see cref="ConfigurationChangeLog"/> entity to a <see cref="ConfigurationChangeLogDto"/>.</summary>
    public static ConfigurationChangeLogDto ToChangeLogDto( ConfigurationChangeLog log ) =>
        new(
            Id: log.Id,
            Key: log.Key,
            PreviousValue: log.PreviousValue,
            NewValue: log.NewValue,
            ChangedByUserId: log.ChangedByUserId,
            ChangedUtc: log.ChangedUtc
        );
}
