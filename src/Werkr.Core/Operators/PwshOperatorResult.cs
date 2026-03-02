namespace Werkr.Core.Operators;

/// <summary>
/// Result of a PowerShell operator execution.
/// </summary>
/// <param name="HadErrors">Whether the PowerShell runtime reported non-terminating errors.</param>
/// <param name="LastExitCode">
/// Value of <c>$LASTEXITCODE</c> after execution, if a native command was invoked.
/// Null when no native command ran during the session.
/// </param>
/// <param name="Exception">Optional exception from a terminating error or infrastructure failure.</param>
public sealed record PwshOperatorResult(
    bool HadErrors,
    int? LastExitCode,
    Exception? Exception = null ) : IOperatorResult {

    /// <summary>
    /// Success is determined by the absence of errors from the PowerShell runtime.
    /// This is the default criterion; tasks may override with custom <c>SuccessCriteria</c>.
    /// </summary>
    public bool Success => !HadErrors && Exception is null;
}
