namespace Werkr.Common.Models;

/// <summary>Step execution tracking DTO for API responses.</summary>
public sealed record StepExecutionDto(
    long Id,
    Guid WorkflowRunId,
    long StepId,
    int Attempt,
    string Status,
    DateTime? StartTime,
    DateTime? EndTime,
    Guid? JobId,
    string? ErrorMessage,
    string? SkipReason
);
