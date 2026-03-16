namespace Werkr.Common.Models;

/// <summary>Dashboard summary for a single workflow.</summary>
public sealed record WorkflowDashboardDto(
    long Id,
    string Name,
    string Description,
    bool Enabled,
    string[]? TargetTags,
    int StepCount,
    int RunCount,
    WorkflowRunSummaryDto? LastRun,
    IReadOnlyList<RunSparklineDto> RecentRuns,
    DateTime? NextScheduledRun
);
