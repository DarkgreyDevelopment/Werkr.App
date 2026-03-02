namespace Werkr.Core.Operators;

/// <summary>
/// Common interface for operator execution results.
/// Provides a uniform way to inspect success/failure status across
/// all operator types (PowerShell, System Shell, Action).
/// </summary>
public interface IOperatorResult {
    /// <summary>Whether the operator execution completed successfully.</summary>
    bool Success { get; }

    /// <summary>
    /// Optional exception captured during execution.
    /// Null when execution succeeds or when the failure is represented
    /// by operator-specific properties (e.g., exit code).
    /// </summary>
    Exception? Exception { get; }
}
