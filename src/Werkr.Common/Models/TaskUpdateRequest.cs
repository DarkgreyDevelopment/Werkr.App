namespace Werkr.Common.Models;

/// <summary>Request DTO for updating an existing task.</summary>
public sealed record TaskUpdateRequest(
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
