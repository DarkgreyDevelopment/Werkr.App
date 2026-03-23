namespace Werkr.Common.Models;

/// <summary>Summary DTO for job list views.</summary>
public sealed record JobListDto(
    Guid Id,
    long TaskId,
    bool Success,
    double RuntimeSeconds,
    DateTime StartTime,
    string ErrorCategory,
    string? TaskName = null,
    Guid? AgentConnectionId = null,
    string? AgentName = null,
    DateTime? EndTime = null
);
