namespace Werkr.Common.Models;

/// <summary>Real-time status update for a single workflow step during execution.</summary>
public sealed record WorkflowStepStatusUpdate(
    Guid RunId,
    long StepId,
    string StepName,
    string Status,
    DateTime Timestamp,
    string? ErrorMessage );
