namespace Werkr.Common.Models;

/// <summary>Operator types available for task execution.</summary>
public enum OperatorType {
    /// <summary>PowerShell operator.</summary>
    PowerShell = 0,

    /// <summary>System shell operator (cmd/bash).</summary>
    SystemShell = 1,

    /// <summary>Built-in action operator.</summary>
    Action = 2,
}
