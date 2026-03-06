namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a new workflow.</summary>
public sealed record WorkflowCreateRequest(
    string Name,
    string? Description = null,
    bool Enabled = true,
    Guid? ScheduleId = null
);
