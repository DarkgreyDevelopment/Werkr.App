namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Categorizes the type of error that occurred during job execution.
/// Designed for expansion — only <see cref="None"/>, <see cref="ScriptError"/>,
/// and <see cref="AgentUnreachable"/> are populated in Phase 5.
/// </summary>
public enum ErrorCategory {
    /// <summary>No error — job completed normally.</summary>
    None = 0,

    /// <summary>Job exceeded its configured timeout.</summary>
    Timeout = 1,

    /// <summary>Agent was unreachable (gRPC transport failure, DNS, timeout).</summary>
    AgentUnreachable = 2,

    /// <summary>Script or command execution error (non-zero exit, exception).</summary>
    ScriptError = 3,

    /// <summary>Insufficient permissions to execute the action.</summary>
    PermissionDenied = 4,

    /// <summary>An unknown or uncategorized error occurred.</summary>
    Unknown = 99,
}
