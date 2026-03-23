namespace Werkr.Common.Models;

/// <summary>Response DTO for a configuration entry.</summary>
public sealed record ConfigurationEntryDto(
    long Id,
    string Key,
    string Value,
    string ValueType,
    string Category,
    string? Description,
    int ScopeLevel,
    string? ScopeId,
    long SyncVersion,
    string? ValidationRules,
    string DefaultValue,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    string ModifiedByUserId
);
