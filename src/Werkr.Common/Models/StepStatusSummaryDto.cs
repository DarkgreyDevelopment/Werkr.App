namespace Werkr.Common.Models;

/// <summary>Lightweight step execution status for DAG overlay coloring.</summary>
public sealed record StepStatusSummaryDto(
    long StepId,
    string Status,
    DateTime? StartTime,
    DateTime? EndTime,
    int? ExitCode
);
