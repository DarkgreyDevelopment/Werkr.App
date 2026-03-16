namespace Werkr.Common.Models;

/// <summary>Lightweight summary of a workflow run for dashboard display.</summary>
public sealed record WorkflowRunSummaryDto(
    Guid Id,
    string Status,
    DateTime StartTime,
    DateTime? EndTime
);
