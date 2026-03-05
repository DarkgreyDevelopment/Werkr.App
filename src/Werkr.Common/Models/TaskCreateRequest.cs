namespace Werkr.Common.Models;

/// <summary>Request DTO for creating a new task.</summary>
public sealed record TaskCreateRequest(
    string Name,
    string? Description,
    string ActionType,
    string Content,
    string[]? Arguments,
    string[] TargetTags,
    bool Enabled = true,
    long? TimeoutMinutes = null,
    string? SuccessCriteria = null,
    Guid? ScheduleId = null,
    long? WorkflowId = null,
    string? ActionSubType = null,
    string? ActionParameters = null
);
