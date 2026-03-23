namespace Werkr.Core.Operators;

/// <summary>
/// Result of a system shell operator execution (cmd.exe / bash).
/// </summary>
/// <param name="ExitCode">The process exit code. Zero typically indicates success.</param>
/// <param name="Exception">Optional exception from a process launch failure or infrastructure error.</param>
public sealed record ShellOperatorResult(
    int ExitCode,
    Exception? Exception = null ) : IOperatorResult {

    /// <summary>
    /// Success is determined by a zero exit code and no exception.
    /// This is the default criterion; tasks may override with custom <c>SuccessCriteria</c>.
    /// </summary>
    public bool Success => ExitCode == 0 && Exception is null;
}
