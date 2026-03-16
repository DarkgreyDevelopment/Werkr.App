namespace Werkr.Common.Models;

/// <summary>Response DTO for the current (latest-version) value of a runtime variable.</summary>
public sealed record RunVariableCurrentDto(
    string Name,
    string Value,
    int Version,
    string Source,
    DateTime Created
);
