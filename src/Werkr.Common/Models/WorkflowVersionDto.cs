namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow version snapshot.</summary>
public sealed record WorkflowVersionDto(
    long Id,
    long WorkflowId,
    int VersionNumber,
    string Definition,
    DateTime CreatedUtc,
    string? CreatedByUserId,
    string? ChangeDescription
);
