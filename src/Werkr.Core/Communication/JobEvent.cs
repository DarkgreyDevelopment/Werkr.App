namespace Werkr.Core.Communication;

/// <summary>
/// Represents a job completion event broadcast to SSE subscribers.
/// Published by <see cref="JobEventBroadcaster"/> when an agent reports a job result.
/// </summary>
/// <param name="JobId">The persisted job's unique identifier.</param>
/// <param name="TaskId">The task that was executed.</param>
/// <param name="WorkflowRunId">Optional workflow run this job belongs to (null for standalone tasks).</param>
/// <param name="Success">Whether the job completed successfully.</param>
/// <param name="ExitCode">Process exit code, if available.</param>
/// <param name="RuntimeSeconds">Total job runtime in seconds.</param>
/// <param name="AgentConnectionId">The agent that executed the job.</param>
/// <param name="Timestamp">UTC timestamp when the event was created.</param>
public sealed record JobEvent(
    Guid JobId,
    long TaskId,
    Guid? WorkflowRunId,
    bool Success,
    int? ExitCode,
    double RuntimeSeconds,
    Guid AgentConnectionId,
    DateTime Timestamp
);
