namespace Werkr.Core.Operators;

/// <summary>
/// Result of a built-in action operator execution.
/// </summary>
/// <param name="Success">Whether the action completed successfully. Defaults to <c>true</c>.</param>
/// <param name="Exception">Optional exception if the action failed.</param>
public sealed record ActionOperatorResult(
    bool Success = true,
    Exception? Exception = null ) : IOperatorResult;
