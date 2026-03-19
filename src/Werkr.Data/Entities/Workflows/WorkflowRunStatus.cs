namespace Werkr.Data.Entities.Workflows;

/// <summary>
/// Status of a workflow execution run.
/// </summary>
public enum WorkflowRunStatus {

    /// <summary>Workflow is currently executing.</summary>
    Running = 0,

    /// <summary>All steps completed successfully.</summary>
    Succeeded = 1,

    /// <summary>One or more steps failed.</summary>
    Failed = 2,

    /// <summary>Workflow execution was cancelled.</summary>
    Cancelled = 3,
}
