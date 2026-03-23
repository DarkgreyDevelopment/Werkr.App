namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a new workflow variable definition.</summary>
public sealed record CreateVariableRequest(
    string Name,
    string? Description = null,
    string? DefaultValue = null,
    string? DataType = null,
    bool IsRequired = false,
    bool LogRedaction = false
);
