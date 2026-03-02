namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow run.</summary>
public sealed record WorkflowRunDto(
    Guid Id,
    long WorkflowId,
    DateTime StartTime,
    DateTime? EndTime,
    string Status );
