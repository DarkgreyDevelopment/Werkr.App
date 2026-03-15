namespace Werkr.Server.Hubs;

/// <summary>DTO pushed to clients when a step transitions state.</summary>
public sealed record StepStatusDto(
    Guid RunId, long StepId, string StepName,
    string Status,
    Guid? JobId, int? ExitCode, double? RuntimeSeconds,
    string? ErrorMessage, DateTime Timestamp,
    int Attempt = 1,
    DateTime? StartTime = null,
    DateTime? EndTime = null );

/// <summary>DTO pushed to clients when a run completes or fails.</summary>
public sealed record RunStatusDto(
    Guid RunId, string Status,
    string? ErrorMessage, long? FailedStepId,
    DateTime Timestamp );

/// <summary>DTO pushed to clients for a new log line on a step.</summary>
public sealed record LogLineDto(
    Guid RunId, long StepId, Guid JobId,
    string Line, DateTime Timestamp );
