using System.Text.RegularExpressions;

using Werkr.Core.Communication;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Evaluates whether a job's execution result satisfies the task's success criteria.
/// When <c>SuccessCriteria</c> is null, defaults are inferred from <see cref="TaskActionType"/>.
/// Custom criteria expressions are predefined string keys evaluated against the typed result.
/// </summary>
/// <param name="logger">Logger instance.</param>
public sealed partial class SuccessCriteriaEvaluator( ILogger<SuccessCriteriaEvaluator> logger ) {

    [GeneratedRegex( @"^exitCode\s*==\s*(-?\d+)$", RegexOptions.IgnoreCase )]
    private static partial Regex ExitCodePattern( );

    /// <summary>
    /// Evaluates success for a completed job.
    /// </summary>
    /// <param name="actionType">The task's action type (determines default criteria).</param>
    /// <param name="successCriteria">
    /// Optional criteria expression. When null, defaults are inferred from <paramref name="actionType"/>.
    /// Supported expressions:
    /// <list type="bullet">
    ///   <item><c>exitCode == N</c> — exit code must equal the specified integer N</item>
    ///   <item><c>pwsh.HadErrors == false</c> — PowerShell had no errors</item>
    ///   <item><c>output.contains("TEXT")</c> — output must contain the specified text</item>
    ///   <item><c>always</c> — always succeed (useful for fire-and-forget tasks)</item>
    /// </list>
    /// </param>
    /// <param name="exitCode">The process/shell exit code, if available.</param>
    /// <param name="output">The collected output lines from the job execution.</param>
    /// <param name="exception">Any exception that occurred during execution.</param>
    /// <returns><c>true</c> if the job meets the success criteria; otherwise <c>false</c>.</returns>
    public bool Evaluate(
        TaskActionType actionType,
        string? successCriteria,
        int? exitCode,
        IReadOnlyList<OperatorOutput> output,
        Exception? exception
    ) {

        // An unhandled exception always means failure, unless criteria is "always"
        if (exception is not null && !string.Equals(
            successCriteria,
            "always",
            StringComparison.OrdinalIgnoreCase
        )) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "Job failed due to exception: {Message}",
                    exception.Message
                );
            }
            return false;
        }

        // If explicit criteria provided, evaluate it
        if (!string.IsNullOrWhiteSpace( successCriteria )) {
            return EvaluateExpression(
                successCriteria,
                exitCode,
                output,
                exception
            );
        }

        // Default criteria based on action type
        return EvaluateDefault(
            actionType,
            exitCode,
            output
        );
    }

    /// <summary>
    /// Returns a human-readable description of the effective success criteria
    /// that will be used for a given task configuration.
    /// </summary>
    /// <param name="actionType">The task's action type.</param>
    /// <param name="successCriteria">The task's explicit criteria, if any.</param>
    /// <returns>A description of what will be evaluated.</returns>
    public static string DescribeEffectiveCriteria(
        TaskActionType actionType,
        string? successCriteria
    ) {
        return !string.IsNullOrWhiteSpace( successCriteria )
            ? successCriteria
            : actionType switch {
                TaskActionType.PowerShellCommand or TaskActionType.PowerShellScript =>
                    "pwsh.HadErrors == false (default)",
                TaskActionType.ShellCommand or TaskActionType.ShellScript =>
                    "exitCode == 0 (default)",
                TaskActionType.Action =>
                    "always (default)",
                _ => "unknown"
            };
    }

    /// <summary>
    /// Evaluates a predefined criteria expression against the result.
    /// </summary>
    private bool EvaluateExpression(
        string criteria,
        int? exitCode,
        IReadOnlyList<OperatorOutput> output,
        Exception? exception
    ) {

        string trimmed = criteria.Trim( );

        // "always" — always succeed
        if (string.Equals(
            trimmed,
            "always",
            StringComparison.OrdinalIgnoreCase
        )) {
            return true;
        }

        // "exitCode == N" — exit code must equal the specified integer
        Match exitCodeMatch = ExitCodePattern().Match(trimmed);
        if (exitCodeMatch.Success && int.TryParse( exitCodeMatch.Groups[1].Value, out int expectedCode )) {
            bool success = exitCode.HasValue && exitCode.Value == expectedCode;
            if (!success && logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "Criteria 'exitCode == {Expected}' failed: exitCode={Actual}.",
                    expectedCode.ToString( ),
                    exitCode?.ToString( ) ?? "null"
                );
            }
            return success;
        }

        // "pwsh.HadErrors == false" — PowerShell had no errors
        if (string.Equals(
            trimmed,
            "pwsh.HadErrors == false",
            StringComparison.OrdinalIgnoreCase
        )) {
            // When server-side: no direct PwshOperatorResult access.
            // We infer from output — Error-level messages indicate HadErrors.
            bool hadErrors = output.Any( o =>
                string.Equals(
                    o.LogLevel,
                    "Error",
                    StringComparison.OrdinalIgnoreCase
                ) );
            bool success = !hadErrors && exception is null;
            if (!success && logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "Criteria 'pwsh.HadErrors == false' failed: " +
                    "hadErrors={HadErrors}, exception={HasException}.",
                    hadErrors.ToString( ),
                    (exception is not null).ToString( )
                );
            }
            return success;
        }

        // "output.contains("TEXT")" — output must contain the specified text
        if (trimmed.StartsWith(
            "output.contains(",
            StringComparison.OrdinalIgnoreCase
        )
            && trimmed.EndsWith( ')' )) {
            string inner = trimmed["output.contains(".Length..^1];
            // Strip surrounding quotes if present
            if (inner.Length >= 2 && inner[0] == '"' && inner[^1] == '"') {
                inner = inner[1..^1];
            }
            bool success = output.Any( o =>
                o.Message.Contains(
                    inner,
                    StringComparison.OrdinalIgnoreCase
                ) );
            if (!success && logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Criteria 'output.contains(\"{Text}\")' failed: text not found in {LineCount} lines.",
                    inner,
                    output.Count.ToString( )
                );
            }
            return success;
        }

        // Unknown criteria — log warning and fall through to default success
        logger.LogWarning(
            "Unknown success criteria expression: '{Criteria}'. Falling back to default.",
            trimmed
        );
        return exitCode is null or 0;
    }

    /// <summary>
    /// Evaluates the default success criteria based on the action type.
    /// </summary>
    private bool EvaluateDefault(
        TaskActionType actionType,
        int? exitCode,
        IReadOnlyList<OperatorOutput> output
    ) {

        return actionType switch {
            // PowerShell: success when no Error-level output
            TaskActionType.PowerShellCommand or TaskActionType.PowerShellScript =>
                !output.Any( o => string.Equals(
                    o.LogLevel,
                    "Error",
                    StringComparison.OrdinalIgnoreCase
                ) ),

            // Shell: success when exit code is 0
            TaskActionType.ShellCommand or TaskActionType.ShellScript =>
                exitCode.HasValue && exitCode.Value == 0,

            // Actions: always succeed (built-in actions handle their own status)
            TaskActionType.Action => true,

            _ => exitCode is null or 0
        };
    }
}
