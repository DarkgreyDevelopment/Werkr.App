namespace Werkr.Common.Models;

/// <summary>Per-step execution status for the most recent workflow run.</summary>
public sealed record WorkflowLatestRunStatusDto(
    Guid? RunId,
    string? RunStatus,
    DateTime? RunStartTime,
    DateTime? RunEndTime,
    IReadOnlyList<StepStatusSummaryDto> Steps
);
