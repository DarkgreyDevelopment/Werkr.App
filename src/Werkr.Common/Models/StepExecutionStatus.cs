namespace Werkr.Common.Models;

/// <summary>
/// Status of a single workflow step execution attempt.
/// Stored in <c>Werkr.Common</c> so both <c>Werkr.Data</c> and <c>Werkr.Server</c> can reference it.
/// </summary>
public enum StepExecutionStatus {

    /// <summary>Step is waiting to execute.</summary>
    Pending = 0,

    /// <summary>Step is currently executing.</summary>
    Running = 1,

    /// <summary>Step completed successfully.</summary>
    Completed = 2,

    /// <summary>Step execution failed.</summary>
    Failed = 3,

    /// <summary>Step was skipped by control flow evaluation.</summary>
    Skipped = 4,
}
