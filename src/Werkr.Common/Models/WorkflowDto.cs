namespace Werkr.Common.Models;

/// <summary>Response DTO for a workflow.</summary>
public sealed record WorkflowDto(
    long Id,
    string Name,
    string Description,
    bool Enabled,
    Guid? ScheduleId,
    IReadOnlyList<WorkflowStepDto> Steps );
