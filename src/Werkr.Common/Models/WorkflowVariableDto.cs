namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow variable definition (design-time).</summary>
public sealed record WorkflowVariableDto(
    long Id,
    long WorkflowId,
    string Name,
    string? Description,
    string? DefaultValue,
    string? DataType,
    bool IsRequired,
    bool LogRedaction
);
