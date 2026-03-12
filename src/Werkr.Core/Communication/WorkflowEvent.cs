namespace Werkr.Core.Communication;

/// <summary>
/// Base record for all workflow execution lifecycle events.
/// Published by <see cref="WorkflowEventBroadcaster"/> and consumed by SSE endpoints.
/// </summary>
public abstract record WorkflowEvent(
    Guid WorkflowRunId,
    DateTime Timestamp
);

/// <summary>Fired when a workflow step begins execution.</summary>
public sealed record StepStartedEvent(
    Guid WorkflowRunId, long StepId, string StepName,
    long TaskId, DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );

/// <summary>Fired when a workflow step completes successfully.</summary>
public sealed record StepCompletedEvent(
    Guid WorkflowRunId, long StepId, string StepName,
    Guid JobId, int ExitCode, double RuntimeSeconds, DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );

/// <summary>Fired when a workflow step fails.</summary>
public sealed record StepFailedEvent(
    Guid WorkflowRunId, long StepId, string StepName,
    Guid JobId, int ExitCode, string? ErrorMessage, DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );

/// <summary>Fired when a workflow step is skipped by control flow evaluation.</summary>
public sealed record StepSkippedEvent(
    Guid WorkflowRunId, long StepId, string StepName,
    string Reason, DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );

/// <summary>Fired when a workflow run completes (success or failure).</summary>
public sealed record RunCompletedEvent(
    Guid WorkflowRunId, bool Success,
    long? FailedStepId, int CompletedSteps, int FailedSteps,
    DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );

/// <summary>Fired when a log line is received for a workflow step.</summary>
public sealed record LogAppendedEvent(
    Guid WorkflowRunId, long StepId, Guid JobId,
    string Line, DateTime Timestamp
) : WorkflowEvent( WorkflowRunId, Timestamp );
