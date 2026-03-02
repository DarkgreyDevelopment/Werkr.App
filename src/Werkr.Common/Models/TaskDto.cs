namespace Werkr.Common.Models;

/// <summary>Response DTO for a task.</summary>
public sealed record TaskDto(
    long Id,
    string Name,
    string Description,
    string ActionType,
    string Content,
    string[]? Arguments,
    string[] TargetTags,
    bool Enabled,
    long? TimeoutMinutes,
    int SyncIntervalMinutes,
    string? SuccessCriteria,
    string EffectiveSuccessCriteria,
    Guid? ScheduleId,
    long? WorkflowId,
    string? ActionSubType = null,
    string? ActionParameters = null );
