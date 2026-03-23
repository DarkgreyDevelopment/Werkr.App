namespace Werkr.Core.Operators;

/// <summary>
/// Result of a built-in action operator execution.
/// </summary>
/// <param name="Success">Whether the action completed successfully. Defaults to <c>true</c>.</param>
/// <param name="Exception">Optional exception if the action failed.</param>
/// <param name="OutputVariableValue">Optional JSON blob produced by the action for variable output.</param>
public sealed record ActionOperatorResult(
    bool Success = true,
    Exception? Exception = null,
    string? OutputVariableValue = null ) : IOperatorResult;
