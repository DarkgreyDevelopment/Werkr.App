namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow run with job details and step executions.</summary>
public sealed record WorkflowRunDetailDto(
    Guid Id,
    long WorkflowId,
    DateTime StartTime,
    DateTime? EndTime,
    string Status,
    IReadOnlyList<JobDto> Jobs,
    IReadOnlyList<StepExecutionDto> StepExecutions
);
