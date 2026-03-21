namespace Werkr.Common.Models;

/// <summary>Response DTO for a configuration change history entry.</summary>
public sealed record ConfigurationChangeLogDto(
    long Id,
    string Key,
    string? PreviousValue,
    string NewValue,
    string ChangedByUserId,
    DateTime ChangedUtc
);
