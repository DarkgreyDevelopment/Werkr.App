namespace Werkr.Common.Models;

/// <summary>Response DTO for a single version-log entry of a runtime variable.</summary>
public sealed record RunVariableVersionDto(
    long Id,
    string VariableName,
    string Value,
    int Version,
    long? ProducedByStepId,
    Guid? ProducedByJobId,
    string Source,
    DateTime Created
);
