using System.Text.RegularExpressions;

using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Evaluates condition expressions against prior workflow step results.
/// Supports multi-dependency evaluation via <see cref="DependencyMode"/> (All/Any).
/// </summary>
/// <param name="logger">Logger instance.</param>
public sealed partial class ConditionEvaluator(
    ILogger<ConditionEvaluator> logger ) {

    /// <summary>
    /// Evaluates a condition expression against a single prior step's result.
    /// </summary>
    /// <param name="expression">The condition expression to evaluate. Null/empty = always true.</param>
    /// <param name="priorJob">The prior step's completed job record.</param>
    /// <returns><c>true</c> if the condition is satisfied.</returns>
    public bool Evaluate(
        string? expression,
        WerkrJob priorJob
    ) {
        if (string.IsNullOrWhiteSpace( expression )) {
            return true;
        }

        string trimmed = expression.Trim( );

        // $? -eq $true / $? -eq $false
        Match successMatch = SuccessRegex( ).Match( trimmed );
        if (successMatch.Success) {
            bool expected = string.Equals(
                successMatch.Groups[1].Value,
                "true",
                StringComparison.OrdinalIgnoreCase
            );
            return priorJob.Success == expected;
        }

        // $exitCode <op> N
        Match exitCodeMatch = ExitCodeRegex( ).Match( trimmed );
        if (exitCodeMatch.Success) {
            string op = exitCodeMatch.Groups[1].Value;
            int value = int.Parse( exitCodeMatch.Groups[2].Value );
            int actual = priorJob.ExitCode ?? 0;

            return op switch {
                "==" => actual == value,
                "!=" => actual != value,
                ">" => actual > value,
                "<" => actual < value,
                ">=" => actual >= value,
                "<=" => actual <= value,
                _ => false,
            };
        }

        // Unknown expression — fail-safe
        logger.LogWarning(
            "Unknown condition expression: '{Expression}'. Returning false.",
            trimmed
        );
        return false;
    }

    /// <summary>
    /// Evaluates a condition expression against multiple predecessor jobs,
    /// applying the step's <see cref="DependencyMode"/> (All or Any).
    /// </summary>
    /// <param name="expression">The condition expression. Null/empty = always true.</param>
    /// <param name="predecessorJobs">The predecessor step job results.</param>
    /// <param name="dependencyMode">How to aggregate per-dependency results.</param>
    /// <returns><c>true</c> if the aggregated condition is satisfied.</returns>
    public bool EvaluateMultiple(
        string? expression,
        IReadOnlyList<WerkrJob> predecessorJobs,
        DependencyMode dependencyMode
    ) {

        if (string.IsNullOrWhiteSpace( expression )) {
            return true;
        }

        if (predecessorJobs.Count == 0) {
            // No predecessors — default: expression against default values
            return Evaluate(
                expression,
                new WerkrJob { Success = true, ExitCode = 0 }
            );
        }

        return dependencyMode switch {
            DependencyMode.All => predecessorJobs.All( job => Evaluate(
                expression,
                job
            ) ),
            DependencyMode.Any => predecessorJobs.Any( job => Evaluate(
                expression,
                job
            ) ),
            _ => predecessorJobs.All( job => Evaluate(
                expression,
                job
            ) ),
        };
    }

    [GeneratedRegex( @"^\$\?\s*-eq\s*\$(true|false)$", RegexOptions.IgnoreCase )]
    private static partial Regex SuccessRegex( );

    [GeneratedRegex( @"^\$exitCode\s*(==|!=|>|<|>=|<=)\s*(-?\d+)$", RegexOptions.IgnoreCase )]
    private static partial Regex ExitCodeRegex( );
}
