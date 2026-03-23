using System.Collections.Concurrent;
using System.Text.Json;
using Werkr.Common.Protos;

namespace Werkr.Agent.Scheduling;

/// <summary>
/// Executes composite workflow nodes (ForEach, While, etc.).
/// Extracted from <see cref="WorkflowExecutionService"/> to keep single-responsibility.
/// The executor resolves the child workflow definition from the synced data and
/// runs it inline within the parent workflow run.
/// </summary>
/// <param name="logger">Logger.</param>
public sealed partial class CompositeNodeExecutor(
    ILogger<CompositeNodeExecutor> logger
) {

    /// <summary>Default iteration guard when none is specified on the step.</summary>
    private const int DefaultMaxIterations = 100;

    /// <summary>
    /// Executes a ForEach composite step: reads a collection variable, iterates over
    /// each element, and runs the child workflow's DAG for each one.
    /// </summary>
    /// <param name="step">The composite step definition from schedule sync.</param>
    /// <param name="childSteps">The child workflow's step definitions.</param>
    /// <param name="workflowRunId">The parent workflow run ID (child runs inline).</param>
    /// <param name="variableCache">The parent's variable cache (child can read + write).</param>
    /// <param name="scheduleId">The parent schedule ID.</param>
    /// <param name="executeStepFunc">
    /// Delegate to execute a single step. Provided by <see cref="WorkflowExecutionService"/>
    /// so that composite execution reuses the same operator pipeline.
    /// </param>
    /// <param name="buildLevelsFunc">Delegate to build topological levels from step defs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating success/failure of the composite execution.</returns>
    internal async Task<CompositeExecutionResult> ExecuteForEachAsync(
        ScheduledWorkflowStepDef step,
        IReadOnlyList<ScheduledWorkflowStepDef> childSteps,
        Guid workflowRunId,
        ConcurrentDictionary<string, string> variableCache,
        Guid? scheduleId,
        Func<ScheduledWorkflowStepDef, Guid, ConcurrentDictionary<long, WorkflowExecutionService.StepJobResult>,
            Dictionary<long, bool>, ConcurrentDictionary<string, string>, Guid?, CancellationToken,
            Task<WorkflowExecutionService.StepExecutionResult>> executeStepFunc,
        Func<IReadOnlyCollection<ScheduledWorkflowStepDef>,
            IReadOnlyList<IReadOnlyList<ScheduledWorkflowStepDef>>> buildLevelsFunc,
        CancellationToken ct
    ) {
        string collectionVarName = step.CollectionVariableName;
        string iterationVarName = step.IterationVariableName;

        if (string.IsNullOrWhiteSpace( collectionVarName )) {
            return CompositeExecutionResult.Fail(
                "ForEach composite step is missing CollectionVariableName." );
        }

        if (string.IsNullOrWhiteSpace( iterationVarName )) {
            return CompositeExecutionResult.Fail(
                "ForEach composite step is missing IterationVariableName." );
        }

        // Read collection variable from cache
        if (!variableCache.TryGetValue( collectionVarName, out string? collectionJson )
            || string.IsNullOrWhiteSpace( collectionJson )) {
            return CompositeExecutionResult.Fail(
                $"Collection variable '{collectionVarName}' not found or empty in variable cache." );
        }

        // Parse as JSON array
        JsonElement[] elements;
        try {
            using JsonDocument doc = JsonDocument.Parse( collectionJson );
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return CompositeExecutionResult.Fail(
                    $"Collection variable '{collectionVarName}' is not a JSON array (found {doc.RootElement.ValueKind})." );
            }
            elements = [.. doc.RootElement.EnumerateArray( )];
        } catch (JsonException ex) {
            return CompositeExecutionResult.Fail(
                $"Failed to parse collection variable '{collectionVarName}' as JSON: {ex.Message}" );
        }

        int maxIterations = step.MaxIterations > 0 ? step.MaxIterations : DefaultMaxIterations;

        if (elements.Length > maxIterations) {
            return CompositeExecutionResult.Fail(
                $"Collection '{collectionVarName}' has {elements.Length} elements, exceeding MaxIterations ({maxIterations})." );
        }

        if (elements.Length == 0) {
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "ForEach step {StepId}: collection '{CollectionVar}' is empty — skipping.",
                    step.StepId, collectionVarName );
            }
            return CompositeExecutionResult.Ok( iterationCount: 0 );
        }

        if (childSteps.Count == 0) {
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "ForEach step {StepId}: child workflow has no steps — skipping.",
                    step.StepId );
            }
            return CompositeExecutionResult.Ok( iterationCount: 0 );
        }

        // Build topological levels for the child workflow
        IReadOnlyList<IReadOnlyList<ScheduledWorkflowStepDef>> childLevels =
            buildLevelsFunc( childSteps );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "ForEach step {StepId}: iterating {Count} elements over {ChildSteps} child steps.",
                step.StepId, elements.Length, childSteps.Count );
        }

        // Execute child DAG for each element sequentially
        for (int i = 0; i < elements.Length; i++) {
            ct.ThrowIfCancellationRequested( );

            // Set iteration variable in the cache (overrides any parent var of the same name)
            string elementValue = elements[i].GetRawText();
            variableCache[iterationVarName] = elementValue;

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "ForEach step {StepId}: iteration {Index}/{Total}, {IterVar}={Value}.",
                    step.StepId, i + 1, elements.Length, iterationVarName,
                    elementValue.Length > 100 ? elementValue[..100] + "..." : elementValue );
            }

            // Execute child DAG levels
            ConcurrentDictionary<long, WorkflowExecutionService.StepJobResult> childStepResults = new();
            Dictionary<long, bool> childBranchTaken = [];

            foreach (IReadOnlyList<ScheduledWorkflowStepDef> level in childLevels) {
                ct.ThrowIfCancellationRequested( );

                foreach (ScheduledWorkflowStepDef childStep in level) {
                    ct.ThrowIfCancellationRequested( );

                    WorkflowExecutionService.StepExecutionResult result = await executeStepFunc(
                        childStep, workflowRunId, childStepResults, childBranchTaken,
                        variableCache, scheduleId, ct );

                    if (result.Job is not null) {
                        childStepResults[result.StepId] = result.Job;
                    }

                    if (result.Failed) {
                        logger.LogWarning(
                            "ForEach step {StepId} iteration {Index}: child step {ChildStepId} failed: {Error}.",
                            step.StepId, i + 1, result.StepId, result.ErrorMessage );
                        return CompositeExecutionResult.Fail(
                            $"ForEach iteration {i + 1} failed at child step {result.StepId}: {result.ErrorMessage}" );
                    }
                }
            }
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "ForEach step {StepId}: all {Count} iterations completed successfully.",
                step.StepId, elements.Length );
        }

        return CompositeExecutionResult.Ok( iterationCount: elements.Length );
    }
}

/// <summary>Result of executing a composite node.</summary>
public sealed record CompositeExecutionResult(
    bool Success,
    int IterationCount,
    string? ErrorMessage
) {
    /// <summary>Creates a successful result.</summary>
    public static CompositeExecutionResult Ok( int iterationCount ) =>
        new( Success: true, IterationCount: iterationCount, ErrorMessage: null );

    /// <summary>Creates a failed result.</summary>
    public static CompositeExecutionResult Fail( string errorMessage ) =>
        new( Success: false, IterationCount: 0, ErrorMessage: errorMessage );
}
