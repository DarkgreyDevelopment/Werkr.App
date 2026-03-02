namespace Werkr.Common.Models;

/// <summary>Response DTO for a job record.</summary>
public sealed record JobDto(
    Guid Id,
    long TaskId,
    bool Success,
    int? ExitCode,
    string ErrorCategory,
    double RuntimeSeconds,
    DateTime StartTime,
    DateTime? EndTime,
    Guid? AgentConnectionId,
    string? Output,
    string? OutputPath );
