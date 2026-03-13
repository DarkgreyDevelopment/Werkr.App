namespace Werkr.Common.Models;

/// <summary>Describes a single filterable field rendered in the FilterBar.</summary>
public sealed record FilterField(
    string Key,
    string Label,
    FilterFieldType Type,
    IReadOnlyList<string>? Options = null
);
