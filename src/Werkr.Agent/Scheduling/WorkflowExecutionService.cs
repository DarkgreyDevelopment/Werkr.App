using System.Collections.Concurrent;
using System.Text.Json;
using Werkr.Agent.Communication;
using Werkr.Agent.Operators;
using Werkr.Common.Models.Actions;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Tasks;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Agent.Scheduling;

/// <summary>
/// Executes workflows locally on the Agent using the same operator infrastructure
/// as <see cref="ScheduleEvaluatorService"/>. Handles DAG-ordered step execution
/// with control flow (If/Else/ElseIf/While/Do), dependency modes, and per-step
/// job reporting. This replaces the server-side <c>WorkflowExecutor</c> that
/// dispatched to agents via gRPC.
/// </summary>
/// <param name="outputWriter">Writes job output to local disk.</param>
/// <param name="successEvaluator">Evaluates success criteria.</param>
/// <param name="conditionEvaluator">Evaluates control flow condition expressions.</param>
/// <param name="pwshOperator">PowerShell operator.</param>
/// <param name="shellOperator">System shell operator.</param>
/// <param name="actionOperator">Built-in action operator.</param>
/// <param name="clientFactory">Factory for creating outbound gRPC clients to the Server.</param>
/// <param name="variableClient">Client for workflow variable get/set/create operations.</param>
/// <param name="outputStreamingService">Manages real-time output streaming to the server.</param>
/// <param name="compositeNodeExecutor">Executor for composite workflow nodes (ForEach, etc.).</param>
/// <param name="serviceScopeFactory">Factory for creating DI scopes to resolve scoped services (e.g. WerkrDbContext).</param>
/// <param name="logger">Logger.</param>
public sealed partial class WorkflowExecutionService(
    AgentJobOutputWriter outputWriter,
    SuccessCriteriaEvaluator successEvaluator,
    ConditionEvaluator conditionEvaluator,
    PwshOperator pwshOperator,
    SystemShellOperator shellOperator,
    IActionOperator actionOperator,
    AgentGrpcClientFactory clientFactory,
    VariableClient variableClient,
    Werkr.Agent.Services.OutputStreamingService outputStreamingService,
    CompositeNodeExecutor compositeNodeExecutor,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<WorkflowExecutionService> logger
) {

    /// <summary>Tracks active CancellationTokenSources keyed by workflow ID for in-flight runs.</summary>
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _activeWorkflows = new();

    private int _activeJobCount;
    private readonly TaskCompletionSource _drainComplete = new();
    private volatile bool _shuttingDown;

    /// <summary>Indicates whether a graceful shutdown is in progress.</summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>
    /// Cancels any in-flight workflow runs for the specified workflow.
    /// Called when the API signals that a workflow was disabled.
    /// </summary>
    public void CancelWorkflow( long workflowId ) {
        if (_activeWorkflows.TryRemove( workflowId, out CancellationTokenSource? cts )) {
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Cancelling in-flight workflow run for WorkflowId={WorkflowId}.", workflowId );
            }
            cts.Cancel( );
        }
    }

    /// <summary>
    /// Initiates a graceful drain: stops accepting new work, waits for active jobs to complete,
    /// and force-cancels after the specified timeout.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for active jobs to finish.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DrainAsync( TimeSpan timeout, CancellationToken ct ) {
        _shuttingDown = true;

        if (Interlocked.CompareExchange( ref _activeJobCount, 0, 0 ) == 0) {
            return;
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Draining {Count} active job(s)...", _activeJobCount );
        }

        try {
            await _drainComplete.Task.WaitAsync( timeout, ct );
        } catch (TimeoutException) {
            logger.LogWarning( "Drain timeout reached after {Timeout}. Force-cancelling active workflows.", timeout );
            foreach (CancellationTokenSource cts in _activeWorkflows.Values) {
                cts.Cancel( );
            }
            // Brief grace period for cancellation to propagate
            await Task.Delay( TimeSpan.FromSeconds( 2 ), CancellationToken.None );
        } catch (OperationCanceledException) {
            // Host forced shutdown
        }
    }

    // ── Result Types ─────────────────────────────────────────────────────────────

    /// <summary>Result of executing a single workflow step.</summary>
    internal sealed record StepExecutionResult(
        long StepId,
        StepJobResult? Job,
        bool Failed,
        bool WasSkipped,
        string? ErrorMessage
    ) {
        public static StepExecutionResult Ok( long stepId, StepJobResult job ) =>
            new( stepId, job, Failed: false, WasSkipped: false, ErrorMessage: null );

        public static StepExecutionResult Skipped( long stepId ) =>
            new( stepId, Job: null, Failed: false, WasSkipped: true, ErrorMessage: null );

        public static StepExecutionResult Fail( long stepId, string errorMessage ) =>
            new( stepId, Job: null, Failed: true, WasSkipped: false, ErrorMessage: errorMessage );
    }

    /// <summary>Captures the essential result of executing a step's task for dependency evaluation.</summary>
    internal sealed record StepJobResult(
        Guid JobId,
        bool Success,
        int ExitCode,
        DateTime StartTime,
        DateTime EndTime,
        ErrorCategory ErrorCategory,
        string? OutputPreview,
        string? OutputVariableValue = null
    );

    // ── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a workflow locally using DAG topological ordering.
    /// Each step runs via the local operator infrastructure and results
    /// are reported to the server individually.
    /// </summary>
    /// <param name="workflow">The workflow definition from the schedule sync.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ExecuteWorkflowLocallyAsync(
        ScheduledWorkflowDefinition workflow,
        CancellationToken ct
    ) {
        if (_shuttingDown) {
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Shutdown in progress — skipping workflow {WorkflowId}.", workflow.WorkflowId );
            }
            return;
        }

        _ = Interlocked.Increment( ref _activeJobCount );
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = _activeWorkflows.TryAdd( workflow.WorkflowId, linkedCts );
        try {
            await ExecuteWorkflowCoreAsync( workflow, linkedCts.Token );
        } finally {
            _ = _activeWorkflows.TryRemove( workflow.WorkflowId, out _ );
            if (Interlocked.Decrement( ref _activeJobCount ) == 0 && _shuttingDown) {
                _ = _drainComplete.TrySetResult( );
            }
        }
    }

    /// <summary>Core workflow execution logic.</summary>
    private async Task ExecuteWorkflowCoreAsync(
        ScheduledWorkflowDefinition workflow,
        CancellationToken ct
    ) {
        Guid workflowRunId;
        if (!string.IsNullOrWhiteSpace( workflow.WorkflowRunId )
            && Guid.TryParse( workflow.WorkflowRunId, out Guid parsedRunId )) {
            workflowRunId = parsedRunId;
        } else {
            // Cron-triggered workflow — ask the API to create the WorkflowRun and seed defaults
            Guid? apiRunId = await variableClient.CreateWorkflowRunAsync(workflow.WorkflowId, ct);
            workflowRunId = apiRunId ?? Guid.NewGuid( ); // Final fallback for backwards compatibility
        }

        // Resolve schedule ID from the workflow definition
        Guid? scheduleId = workflow.Schedule is not null
            && Guid.TryParse( workflow.Schedule.ScheduleId, out Guid sid )
                ? sid : null;

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Starting local workflow execution {RunId} for workflow {WorkflowId} '{WorkflowName}' ({StepCount} steps).",
                workflowRunId, workflow.WorkflowId, workflow.Name, workflow.Steps.Count );
        }

        // Seed variable cache from workflow definition defaults and trigger variables
        ConcurrentDictionary<string, string> variableCache = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowVariableDef varDef in workflow.Variables) {
            if (!string.IsNullOrWhiteSpace( varDef.DefaultValue )) {
                variableCache[varDef.Name] = varDef.DefaultValue;
            }
        }
        foreach (KeyValuePair<string, string> trigger in workflow.TriggerVariables) {
            variableCache[trigger.Key] = trigger.Value;
        }

        // Seed from prior run variable values (retry-from-failed: includes outputs from succeeded steps)
        foreach (KeyValuePair<string, string> rv in workflow.RunVariableValues) {
            variableCache[rv.Key] = rv.Value;
        }

        // Build topological levels from the step definitions
        IReadOnlyList<IReadOnlyList<ScheduledWorkflowStepDef>> levels =
            BuildTopologicalLevels( workflow.Steps );

        // Build child workflow lookup for composite steps (keyed by child workflow ID)
        Dictionary<long, ChildWorkflowDefinition> childWorkflowMap = [];
        foreach (ChildWorkflowDefinition child in workflow.ChildWorkflows) {
            childWorkflowMap[child.ChildWorkflowId] = child;
        }

        // Step result map: stepId → completed job result (concurrent for parallel steps)
        ConcurrentDictionary<long, StepJobResult> stepResults = new();

        // Pre-populate stepResults with prior succeeded steps (retry-from-failed scenario).
        // This allows dependency checks to pass for upstream steps that don't need re-execution.
        foreach (long succeededStepId in workflow.PriorSucceededStepIds) {
            stepResults[succeededStepId] = new StepJobResult(
                JobId: Guid.Empty,
                Success: true,
                ExitCode: 0,
                StartTime: DateTime.MinValue,
                EndTime: DateTime.MinValue,
                ErrorCategory: ErrorCategory.None,
                OutputPreview: null );
        }

        // If/Else/ElseIf chain tracking: stepId → whether that branch was taken
        Dictionary<long, bool> branchTaken = [];

        bool workflowFailed = false;
        long? failedStepId = null;

        try {
            foreach (IReadOnlyList<ScheduledWorkflowStepDef> level in levels) {
                ct.ThrowIfCancellationRequested( );

                // Partition steps into chain-bound (If/Else/ElseIf) and parallelizable
                List<ScheduledWorkflowStepDef> parallelizable = [];
                List<ScheduledWorkflowStepDef> chainBound = [];

                foreach (ScheduledWorkflowStepDef step in level) {
                    ControlStatement cs = (ControlStatement) step.ControlStatement;
                    if (cs is ControlStatement.If or ControlStatement.Else or ControlStatement.ElseIf) {
                        chainBound.Add( step );
                    } else {
                        parallelizable.Add( step );
                    }
                }

                // Execute parallelizable steps concurrently via Task.WhenAll
                if (parallelizable.Count > 0) {
                    StepExecutionResult[] parallelResults = await Task.WhenAll(
                        parallelizable.Select( step => ExecuteStepAsync(
                            step, workflowRunId, stepResults, branchTaken, variableCache, scheduleId, childWorkflowMap, ct ) ) );

                    foreach (StepExecutionResult result in parallelResults) {
                        if (result.Job is not null) {
                            stepResults[result.StepId] = result.Job;
                        }

                        if (result.Failed) {
                            logger.LogWarning(
                                "Workflow run {RunId} failed at step {StepId}: {Error}.",
                                workflowRunId, result.StepId, result.ErrorMessage );
                            workflowFailed = true;
                            failedStepId ??= result.StepId;
                        }
                    }
                }

                if (workflowFailed) {
                    break;
                }

                // Execute chain-bound steps sequentially (order matters for If/Else evaluation)
                foreach (ScheduledWorkflowStepDef step in chainBound) {
                    ct.ThrowIfCancellationRequested( );

                    StepExecutionResult result = await ExecuteStepAsync(
                        step, workflowRunId, stepResults, branchTaken, variableCache, scheduleId, childWorkflowMap, ct);

                    if (result.Job is not null) {
                        stepResults[result.StepId] = result.Job;
                    }

                    if (result.Failed) {
                        logger.LogWarning(
                            "Workflow run {RunId} failed at step {StepId}: {Error}.",
                            workflowRunId, result.StepId, result.ErrorMessage );
                        workflowFailed = true;
                        failedStepId ??= result.StepId;
                        break;
                    }
                }

                if (workflowFailed) {
                    break;
                }
            }

            if (!workflowFailed && logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Workflow run {RunId} completed successfully ({StepCount} steps executed).",
                    workflowRunId, stepResults.Count );
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Workflow run {RunId} was cancelled.", workflowRunId );
            }
            workflowFailed = true;
        } catch (Exception ex) {
            logger.LogError( ex, "Workflow run {RunId} failed with unexpected error.", workflowRunId );
            workflowFailed = true;
        }

        // Report workflow run completion/failure to the server.
        // Use a non-cancellable token — we must report even if the run was cancelled (disabled).
        await CompleteWorkflowRunAsync( workflowRunId, !workflowFailed, failedStepId, CancellationToken.None );
    }

    // ── Step Execution ───────────────────────────────────────────────────────────

    /// <summary>Executes a single workflow step, handling control flow, composite nodes, and variable I/O.</summary>
    internal async Task<StepExecutionResult> ExecuteStepAsync(
        ScheduledWorkflowStepDef step,
        Guid workflowRunId,
        ConcurrentDictionary<long, StepJobResult> stepResults,
        Dictionary<long, bool> branchTaken,
        ConcurrentDictionary<string, string> variableCache,
        Guid? scheduleId,
        Dictionary<long, ChildWorkflowDefinition> childWorkflowMap,
        CancellationToken ct
    ) {
        // Handle composite steps (ForEach, etc.) before task validation
        if (step.IsComposite && (CompositeType)step.CompositeType == CompositeType.ForEach) {
            return await ExecuteCompositeStepAsync(
                step, workflowRunId, stepResults, variableCache, scheduleId, childWorkflowMap, ct );
        }

        ScheduledTaskDefinition? taskDef = step.Task;
        if (taskDef is null) {
            string msg = $"Step {step.StepId} has no embedded task definition.";
            return StepExecutionResult.Fail( step.StepId, msg );
        }

        // Skip steps that already succeeded in a prior attempt (retry-from-failed)
        if (stepResults.ContainsKey( step.StepId )) {
            return StepExecutionResult.Skipped( step.StepId );
        }

        string stepLabel = $"Step {step.Order}: {taskDef.Name}";

        // Check dependency satisfaction
        if (!CheckDependencies( step, stepResults )) {
            string depError = $"Dependencies not satisfied for step {step.StepId} (DependencyMode={(DependencyMode) step.DependencyMode}).";
            return StepExecutionResult.Fail( step.StepId, depError );
        }

        // Gather predecessor jobs for condition evaluation
        List<WerkrJob> predecessorJobs = BuildPredecessorJobs( step, stepResults );

        // Evaluate control flow
        bool shouldExecute = EvaluateControlFlow( step, predecessorJobs, branchTaken );

        if (!shouldExecute) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Step {StepId} '{StepLabel}' skipped by control flow ({ControlStatement}).",
                    step.StepId, stepLabel, (ControlStatement)step.ControlStatement );
            }

            string skipReason = $"Control flow: {(ControlStatement) step.ControlStatement}";
            await ReportStepSkippedAsync( workflowRunId, step.StepId, taskDef.Name, skipReason, ct );

            return StepExecutionResult.Skipped( step.StepId );
        }

        // Report step started to the server
        await ReportStepStartedAsync( workflowRunId, step.StepId, taskDef.Name, taskDef.TaskId, ct );

        // Resolve input variable from cache
        string? inputVariableValue = null;
        if (!string.IsNullOrWhiteSpace( step.InputVariableName )
            && variableCache.TryGetValue( step.InputVariableName, out string? cachedValue )) {
            inputVariableValue = cachedValue;
        }

        string? outputVariableName = string.IsNullOrWhiteSpace(step.OutputVariableName)
            ? null : step.OutputVariableName;

        // Handle While/Do loops
        ControlStatement cs = (ControlStatement) step.ControlStatement;
        if (cs is ControlStatement.While or ControlStatement.Do) {
            return await ExecuteLoopStepAsync(
                step, taskDef, workflowRunId, predecessorJobs, variableCache, scheduleId, ct );
        }

        // Execute the step's task locally
        StepJobResult job = await ExecuteStepTaskAsync(
            taskDef, workflowRunId, step.StepId, scheduleId, inputVariableValue, outputVariableName, ct);

        // Push output variable to server and local cache
        if (outputVariableName is not null && job.Success) {
            string value = job.OutputVariableValue ?? string.Empty;
            variableCache[outputVariableName] = value;
            await variableClient.PushVariableAsync( workflowRunId, outputVariableName,
                value, step.StepId, job.JobId, ct );
        }

        // Record branch taken for If/ElseIf chains
        if (cs is ControlStatement.If or ControlStatement.ElseIf) {
            branchTaken[step.StepId] = true;
        }

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Step {StepId} '{StepLabel}' executed: Success={Success}, JobId={JobId}.",
                step.StepId, stepLabel, job.Success, job.JobId );
        }

        return StepExecutionResult.Ok( step.StepId, job );
    }

    /// <summary>
    /// Executes a composite step by delegating to <see cref="CompositeNodeExecutor"/>.
    /// Reports step started/completed lifecycle events to the server.
    /// </summary>
    private async Task<StepExecutionResult> ExecuteCompositeStepAsync(
        ScheduledWorkflowStepDef step,
        Guid workflowRunId,
        ConcurrentDictionary<long, StepJobResult> stepResults,
        ConcurrentDictionary<string, string> variableCache,
        Guid? scheduleId,
        Dictionary<long, ChildWorkflowDefinition> childWorkflowMap,
        CancellationToken ct
    ) {
        // Skip steps that already succeeded in a prior attempt (retry-from-failed)
        if (stepResults.ContainsKey( step.StepId )) {
            return StepExecutionResult.Skipped( step.StepId );
        }

        // Check dependency satisfaction
        if (!CheckDependencies( step, stepResults )) {
            string depError = $"Dependencies not satisfied for composite step {step.StepId}.";
            return StepExecutionResult.Fail( step.StepId, depError );
        }

        // Resolve child workflow steps
        if (step.ChildWorkflowId == 0
            || !childWorkflowMap.TryGetValue( step.ChildWorkflowId, out ChildWorkflowDefinition? childDef )) {
            return StepExecutionResult.Fail( step.StepId,
                $"Child workflow definition not found for composite step {step.StepId} (ChildWorkflowId={step.ChildWorkflowId})." );
        }

        // Report step started
        string stepName = $"ForEach (Step {step.Order})";
        await ReportStepStartedAsync( workflowRunId, step.StepId, stepName, 0, ct );

        DateTime startTime = DateTime.UtcNow;

        // Empty child workflow map for child steps (no nested composite in POC)
        Dictionary<long, ChildWorkflowDefinition> emptyChildMap = [];

        CompositeExecutionResult compositeResult = await compositeNodeExecutor.ExecuteForEachAsync(
            step,
            [.. childDef.Steps],
            workflowRunId,
            variableCache,
            scheduleId,
            ( s, runId, results, branch, cache, sId, token ) =>
                ExecuteStepAsync( s, runId, results, branch, cache, sId, emptyChildMap, token ),
            BuildTopologicalLevels,
            ct );

        DateTime endTime = DateTime.UtcNow;

        // Create a synthetic job result for the composite step
        StepJobResult syntheticJob = new(
            JobId: Guid.NewGuid( ),
            Success: compositeResult.Success,
            ExitCode: compositeResult.Success ? 0 : 1,
            StartTime: startTime,
            EndTime: endTime,
            ErrorCategory: compositeResult.Success ? ErrorCategory.None : ErrorCategory.ScriptError,
            OutputPreview: compositeResult.Success
                ? $"ForEach: {compositeResult.IterationCount} iteration(s) completed."
                : compositeResult.ErrorMessage );

        // Report the composite step result to the server
        await ReportJobResultAsync(
            syntheticJob.JobId,
            new ScheduledTaskDefinition { TaskId = 0, Name = stepName },
            startTime, endTime,
            compositeResult.Success,
            syntheticJob.ExitCode,
            syntheticJob.ErrorCategory,
            workflowRunId.ToString( ),
            syntheticJob.OutputPreview,
            scheduleId,
            step.StepId,
            outputVariableName: null,
            outputVariableValue: null,
            ct );

        return compositeResult.Success
            ? StepExecutionResult.Ok( step.StepId, syntheticJob )
            : StepExecutionResult.Fail( step.StepId, compositeResult.ErrorMessage ?? "ForEach execution failed." );
    }

    /// <summary>Executes a While or Do loop step with variable support.</summary>
    private async Task<StepExecutionResult> ExecuteLoopStepAsync(
        ScheduledWorkflowStepDef step,
        ScheduledTaskDefinition taskDef,
        Guid workflowRunId,
        List<WerkrJob> predecessorJobs,
        ConcurrentDictionary<string, string> variableCache,
        Guid? scheduleId,
        CancellationToken ct
    ) {
        StepJobResult? lastJob = null;
        int iterations = 0;
        bool isDoLoop = (ControlStatement) step.ControlStatement == ControlStatement.Do;
        int maxIterations = step.MaxIterations > 0 ? step.MaxIterations : 100;

        // Resolve input/output variable names
        string? inputVariableValue = null;
        if (!string.IsNullOrWhiteSpace( step.InputVariableName )
            && variableCache.TryGetValue( step.InputVariableName, out string? cachedIn )) {
            inputVariableValue = cachedIn;
        }
        string? outputVariableName = string.IsNullOrWhiteSpace(step.OutputVariableName)
            ? null : step.OutputVariableName;

        while (iterations < maxIterations) {
            ct.ThrowIfCancellationRequested( );

            // For While: check condition before execution
            // For Do: execute first, then check condition
            if (!isDoLoop || iterations > 0) {
                IReadOnlyList<WerkrJob> evalJobs = lastJob is not null
                    ? [ToWerkrJob( lastJob )]
                    : predecessorJobs;
                bool conditionMet = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, evalJobs, (DependencyMode) step.DependencyMode );
                if (!conditionMet) {
                    break;
                }
            }

            // Re-read input variable from cache on each iteration (may have been updated)
            if (!string.IsNullOrWhiteSpace( step.InputVariableName )
                && variableCache.TryGetValue( step.InputVariableName, out string? loopInput )) {
                inputVariableValue = loopInput;
            }

            lastJob = await ExecuteStepTaskAsync(
                taskDef, workflowRunId, step.StepId, scheduleId, inputVariableValue, outputVariableName, ct );
            iterations++;

            // Push output variable after each iteration
            if (outputVariableName is not null && lastJob.Success) {
                string value = lastJob.OutputVariableValue ?? string.Empty;
                variableCache[outputVariableName] = value;
                await variableClient.PushVariableAsync( workflowRunId, outputVariableName,
                    value, step.StepId, lastJob.JobId, ct );
            }

            if (!lastJob.Success) {
                break;
            }
        }

        if (iterations >= maxIterations) {
            logger.LogWarning( "Step {StepId} reached MaxIterations ({Max}).",
                step.StepId, maxIterations );
        }

        return lastJob is not null
            ? StepExecutionResult.Ok( step.StepId, lastJob )
            : StepExecutionResult.Skipped( step.StepId );
    }

    // ── Task Execution (mirrors ScheduleEvaluatorService.ExecuteTaskLocallyAsync) ──

    /// <summary>
    /// Executes a single task locally with variable I/O support and reports the result to the server.
    /// </summary>
    private async Task<StepJobResult> ExecuteStepTaskAsync(
        ScheduledTaskDefinition taskDef,
        Guid workflowRunId,
        long stepId,
        Guid? scheduleId,
        string? inputVariableValue,
        string? outputVariableName,
        CancellationToken ct
    ) {
        Guid jobId = Guid.NewGuid();
        DateTime startTime = DateTime.UtcNow;

        TaskActionType actionType = (TaskActionType) taskDef.ActionType;
        List<OperatorOutput> collectedOutput = [];
        int exitCode = 0;
        Exception? executionException = null;
        ErrorCategory errorCategory = ErrorCategory.None;
        string? outputVariableValue = null;

        // Variable file I/O for shell operators
        bool isShellAction = actionType is TaskActionType.PowerShellCommand
            or TaskActionType.PowerShellScript
            or TaskActionType.ShellCommand
            or TaskActionType.ShellScript;

        string? inputFilePath = null;
        string? outputFilePath = null;
        Dictionary<string, string>? envVars = null;

        if (isShellAction && (inputVariableValue is not null || outputVariableName is not null)) {
            string varsDir = Path.Combine("job-output", "_vars");
            _ = Directory.CreateDirectory( varsDir );
            envVars = [];

            if (inputVariableValue is not null) {
                inputFilePath = Path.Combine( varsDir, $"{jobId}_input.json" );
                await File.WriteAllTextAsync( inputFilePath, inputVariableValue, ct );
                envVars["WERKR_INPUT"] = Path.GetFullPath( inputFilePath );
            }

            if (outputVariableName is not null) {
                outputFilePath = Path.Combine( varsDir, $"{jobId}_output.json" );
                envVars["WERKR_OUTPUT"] = Path.GetFullPath( outputFilePath );
            }
        }

        try {
            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct);

            if (taskDef.TimeoutMinutes > 0) {
                timeoutCts.CancelAfter( TimeSpan.FromMinutes( taskDef.TimeoutMinutes ) );
            }

            OperatorExecution execution = RunOperator(
                taskDef, actionType, envVars, inputVariableValue, timeoutCts.Token);

            await foreach (OperatorOutput output in execution.Output.WithCancellation( timeoutCts.Token )) {
                await outputWriter.WriteLineAsync( jobId, output, timeoutCts.Token );
                collectedOutput.Add( output );

                // Publish to output streaming service with workflow context
                string scheduleIdStr = scheduleId?.ToString( ) ?? "";
                outputStreamingService.Publish( new OutputMessage {
                    TaskId = taskDef.TaskId,
                    ScheduleId = scheduleIdStr,
                    JobId = jobId.ToString( ),
                    Line = new OutputLine {
                        Text = output.Message,
                        LogLevel = output.LogLevel,
                        Timestamp = output.Timestamp,
                    },
                    WorkflowRunId = workflowRunId.ToString( ),
                    StepId = stepId,
                } );
            }

            IOperatorResult result = await execution.Result;
            exitCode = result switch {
                ShellOperatorResult shell => shell.ExitCode,
                PwshOperatorResult pwsh => pwsh.LastExitCode ?? (pwsh.HadErrors ? 1 : 0),
                _ => result.Success ? 0 : 1,
            };
            executionException = result.Exception;

            if (!result.Success) {
                errorCategory = ErrorCategory.ScriptError;
            }

            // Read output variable from action result or output file
            if (result is ActionOperatorResult actionResult && actionResult.OutputVariableValue is not null) {
                outputVariableValue = actionResult.OutputVariableValue;
            } else if (outputFilePath is not null && File.Exists( outputFilePath )) {
                outputVariableValue = await File.ReadAllTextAsync( outputFilePath, ct );
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw; // Propagate shutdown
        } catch (OperationCanceledException) {
            errorCategory = ErrorCategory.Timeout;
            exitCode = -1;
            OperatorOutput timeoutMsg = OperatorOutput.Create( "Error",
                $"Task '{taskDef.Name}' exceeded timeout of {taskDef.TimeoutMinutes} minutes." );
            await outputWriter.WriteLineAsync( jobId, timeoutMsg, ct );
            collectedOutput.Add( timeoutMsg );
        } catch (Exception ex) {
            executionException = ex;
            errorCategory = ErrorCategory.ScriptError;
            exitCode = -1;
            OperatorOutput errorMsg = OperatorOutput.Create( "Error", $"Execution error: {ex.Message}" );
            await outputWriter.WriteLineAsync( jobId, errorMsg, ct );
            collectedOutput.Add( errorMsg );
        } finally {
            // Cleanup temporary variable files
            if (inputFilePath is not null) {
                try { File.Delete( inputFilePath ); } catch { /* best-effort cleanup */ }
            }
            if (outputFilePath is not null) {
                try { File.Delete( outputFilePath ); } catch { /* best-effort cleanup */ }
            }
        }

        DateTime endTime = DateTime.UtcNow;

        bool success = successEvaluator.Evaluate(
            actionType,
            string.IsNullOrWhiteSpace( taskDef.SuccessCriteria ) ? null : taskDef.SuccessCriteria,
            exitCode,
            collectedOutput,
            executionException );

        string? tailPreview = await outputWriter.GetTailPreviewAsync( jobId, ct );

        // Persist job locally in the agent's SQLite database
        await PersistJobLocallyAsync( jobId, taskDef.TaskId, taskDef.Content, startTime, endTime,
            success, exitCode, errorCategory, tailPreview, workflowRunId.ToString( ), scheduleId, ct );

        // Report result to server with workflowRunId, schedule ID, step ID, and output variable info
        await ReportJobResultAsync(
            jobId, taskDef, startTime, endTime, success, exitCode,
            errorCategory, workflowRunId.ToString( ), tailPreview, scheduleId,
            stepId, outputVariableName, outputVariableValue, ct );

        // Publish completion to output streaming service with workflow context
        string completeScheduleIdStr = scheduleId?.ToString( ) ?? "";
        outputStreamingService.Publish( new OutputMessage {
            TaskId = taskDef.TaskId,
            ScheduleId = completeScheduleIdStr,
            JobId = jobId.ToString( ),
            Complete = new OutputComplete {
                ExitCode = exitCode,
                Success = success,
                ErrorMessage = executionException?.Message ?? "",
            },
            WorkflowRunId = workflowRunId.ToString( ),
            StepId = stepId,
        } );

        return new StepJobResult( jobId, success, exitCode, startTime, endTime, errorCategory, tailPreview, outputVariableValue );
    }

    // ── Operator Selection (mirrors ScheduleEvaluatorService.RunOperator) ────────

    /// <summary>
    /// Selects and invokes the appropriate operator based on the task's action type,
    /// passing environment variables for shell operators and input variable values for actions.
    /// </summary>
    private OperatorExecution RunOperator(
        ScheduledTaskDefinition taskDef,
        TaskActionType actionType,
        IReadOnlyDictionary<string, string>? environmentVariables,
        string? inputVariableValue,
        CancellationToken ct
    ) {
        if (actionType == TaskActionType.Action) {
            using JsonDocument parsedParameters = JsonDocument.Parse( taskDef.ActionParametersJson );
            ActionDescriptor descriptor = new() {
                Action = taskDef.ActionSubType,
                Parameters = parsedParameters.RootElement.Clone(),
            };

            return actionOperator.Execute( descriptor, inputVariableValue, ct );
        }

        IShellOperator shellOp = actionType switch
        {
            TaskActionType.PowerShellCommand or TaskActionType.PowerShellScript => pwshOperator,
            TaskActionType.ShellCommand or TaskActionType.ShellScript => shellOperator,
            _ => throw new NotSupportedException(
                $"ActionType '{actionType}' is not supported for local execution." ),
        };

        return actionType switch {
            TaskActionType.PowerShellCommand or TaskActionType.ShellCommand =>
                shellOp.RunCommand( taskDef.Content, environmentVariables, ct ),

            TaskActionType.PowerShellScript or TaskActionType.ShellScript when taskDef.Arguments.Count > 0 =>
                shellOp.RunScriptWithArgs( taskDef.Content, taskDef.Arguments, environmentVariables, ct ),

            TaskActionType.PowerShellScript or TaskActionType.ShellScript =>
                shellOp.RunScript( taskDef.Content, environmentVariables, ct ),

            _ => throw new NotSupportedException(
                $"ActionType '{actionType}' is not supported for local execution." ),
        };
    }

    // ── DAG Topological Sort ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds topological levels from proto step definitions using Kahn's algorithm.
    /// Steps at the same level have all dependencies satisfied by prior levels.
    /// </summary>
    internal static List<IReadOnlyList<ScheduledWorkflowStepDef>> BuildTopologicalLevels(
        IReadOnlyCollection<ScheduledWorkflowStepDef> steps
    ) {
        // Build adjacency: stepId → set of dependents
        Dictionary<long, List<long>> dependents = [];
        Dictionary<long, int> inDegree = [];
        Dictionary<long, ScheduledWorkflowStepDef> stepMap = [];

        foreach (ScheduledWorkflowStepDef step in steps) {
            stepMap[step.StepId] = step;
            _ = inDegree.TryAdd( step.StepId, 0 );
            _ = dependents.TryAdd( step.StepId, [] );
        }

        foreach (ScheduledWorkflowStepDef step in steps) {
            foreach (long depId in step.DependsOnStepIds) {
                if (dependents.TryGetValue( depId, out List<long>? depList )) {
                    depList.Add( step.StepId );
                }
                inDegree[step.StepId] = inDegree.GetValueOrDefault( step.StepId ) + 1;
            }
        }

        List<IReadOnlyList<ScheduledWorkflowStepDef>> levels = [];
        Queue<long> queue = new();

        foreach ((long id, int deg) in inDegree) {
            if (deg == 0) {
                queue.Enqueue( id );
            }
        }

        while (queue.Count > 0) {
            List<ScheduledWorkflowStepDef> level = [];
            int levelSize = queue.Count;

            for (int i = 0; i < levelSize; i++) {
                long id = queue.Dequeue();
                level.Add( stepMap[id] );

                foreach (long depId in dependents.GetValueOrDefault( id, [] )) {
                    inDegree[depId]--;
                    if (inDegree[depId] == 0) {
                        queue.Enqueue( depId );
                    }
                }
            }

            // Sort by order within level for deterministic execution
            level.Sort( ( a, b ) => a.Order.CompareTo( b.Order ) );
            levels.Add( level );
        }

        return levels;
    }

    // ── Control Flow ─────────────────────────────────────────────────────────────

    /// <summary>Checks whether dependencies are satisfied based on DependencyMode.</summary>
    private static bool CheckDependencies(
        ScheduledWorkflowStepDef step,
        ConcurrentDictionary<long, StepJobResult> stepResults
    ) {
        if (step.DependsOnStepIds.Count == 0) {
            return true; // Root step — no dependencies
        }

        DependencyMode mode = (DependencyMode) step.DependencyMode;

        return mode switch {
            DependencyMode.AllSuccess =>
                step.DependsOnStepIds.All( stepResults.ContainsKey ),
            DependencyMode.AnySuccess =>
                step.DependsOnStepIds.Any( stepResults.ContainsKey ),
            _ => step.DependsOnStepIds.All( stepResults.ContainsKey ),
        };
    }

    /// <summary>Evaluates control flow to determine if a step should execute.</summary>
    private bool EvaluateControlFlow(
        ScheduledWorkflowStepDef step,
        List<WerkrJob> predecessorJobs,
        Dictionary<long, bool> branchTaken
    ) {
        ControlStatement cs = (ControlStatement) step.ControlStatement;

        switch (cs) {
            case ControlStatement.Default:
                return true;

            case ControlStatement.If:
                bool ifResult = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, predecessorJobs, (DependencyMode) step.DependencyMode );
                branchTaken[step.StepId] = ifResult;
                return ifResult;

            case ControlStatement.ElseIf: {
                    bool priorTaken = step.DependsOnStepIds.Any( id =>
                    branchTaken.TryGetValue( id, out bool taken ) && taken );
                    if (priorTaken) {
                        branchTaken[step.StepId] = false;
                        return false;
                    }
                    bool elseIfResult = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, predecessorJobs, (DependencyMode) step.DependencyMode );
                    branchTaken[step.StepId] = elseIfResult;
                    return elseIfResult;
                }

            case ControlStatement.Else: {
                    bool anyPriorTaken = step.DependsOnStepIds.Any( id =>
                    branchTaken.TryGetValue( id, out bool taken ) && taken );
                    return !anyPriorTaken;
                }

            case ControlStatement.While:
            case ControlStatement.Do:
                return true;

            default:
                return true;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds lightweight <see cref="WerkrJob"/> instances from step results
    /// for use by the <see cref="ConditionEvaluator"/> which expects WerkrJob inputs.
    /// </summary>
    private static List<WerkrJob> BuildPredecessorJobs(
        ScheduledWorkflowStepDef step,
        ConcurrentDictionary<long, StepJobResult> stepResults
    ) {
        List<WerkrJob> jobs = [];
        foreach (long depId in step.DependsOnStepIds) {
            if (stepResults.TryGetValue( depId, out StepJobResult? result )) {
                jobs.Add( ToWerkrJob( result ) );
            }
        }
        return jobs;
    }

    /// <summary>
    /// Converts a <see cref="StepJobResult"/> to a lightweight <see cref="WerkrJob"/>
    /// with just enough data for condition evaluation.
    /// </summary>
    private static WerkrJob ToWerkrJob( StepJobResult result ) => new( ) {
        Id = result.JobId,
        Success = result.Success,
        ExitCode = result.ExitCode,
        StartTime = result.StartTime,
        EndTime = result.EndTime,
        ErrorCategory = result.ErrorCategory,
    };

    // ── Local Job Persistence ────────────────────────────────────────────────────

    /// <summary>
    /// Persists a completed job to the agent's local SQLite database.
    /// Creates a DI scope to resolve a scoped <see cref="WerkrDbContext"/>.
    /// </summary>
    private async Task PersistJobLocallyAsync(
        Guid jobId,
        long taskId,
        string taskSnapshot,
        DateTime startTime,
        DateTime endTime,
        bool success,
        int exitCode,
        ErrorCategory errorCategory,
        string? outputPreview,
        string? workflowRunId,
        Guid? scheduleId,
        CancellationToken ct
    ) {
        try {
            await using AsyncServiceScope scope = serviceScopeFactory.CreateAsyncScope( );
            WerkrDbContext dbContext = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

            Data.Entities.Registration.RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

            WerkrJob job = new( ) {
                Id = jobId,
                TaskId = taskId,
                TaskSnapshot = taskSnapshot,
                RuntimeSeconds = ( endTime - startTime ).TotalSeconds,
                StartTime = startTime,
                EndTime = endTime,
                Success = success,
                AgentConnectionId = connection.Id,
                ExitCode = exitCode,
                ErrorCategory = errorCategory,
                Output = outputPreview,
                OutputPath = AgentJobOutputWriter.GetRelativeOutputPath( jobId ),
                ScheduleId = scheduleId,
            };

            if (!string.IsNullOrWhiteSpace( workflowRunId ) && Guid.TryParse( workflowRunId, out Guid wfRunId )) {
                job.WorkflowRunId = wfRunId;
            }

            _ = dbContext.Jobs.Add( job );
            _ = await dbContext.SaveChangesAsync( ct );

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Persisted step job {JobId} locally for task {TaskId}.", jobId, taskId );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to persist step job {JobId} locally. Server report will still be attempted.", jobId );
        }
    }

    // ── Job Reporting ────────────────────────────────────────────────────────────

    /// <summary>Maximum number of retry attempts for reporting a job result.</summary>
    private const int ReportMaxRetries = 3;

    /// <summary>Initial delay between retry attempts.</summary>
    private static readonly TimeSpan s_reportRetryBaseDelay = TimeSpan.FromSeconds( 2 );

    /// <summary>
    /// Reports a completed job result to the Server via gRPC with retry on transient failures.
    /// Includes output variable information when a step produces variable output.
    /// </summary>
    private async Task ReportJobResultAsync(
        Guid jobId,
        ScheduledTaskDefinition taskDef,
        DateTime startTime,
        DateTime endTime,
        bool success,
        int exitCode,
        ErrorCategory errorCategory,
        string? workflowRunId,
        string? outputPreview,
        Guid? scheduleId,
        long stepId,
        string? outputVariableName,
        string? outputVariableValue,
        CancellationToken ct
    ) {
        JobReporting.JobReportingClient client = await clientFactory.CreateJobReportingClientAsync( ct );
        Data.Entities.Registration.RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

        JobResultRequest innerRequest = new() {
            ConnectionId = connection.Id.ToString(),
            TaskId = taskDef.TaskId,
            TaskSnapshot = taskDef.Content,
            RuntimeSeconds = ( endTime - startTime ).TotalSeconds,
            StartTime = startTime.ToString( "o" ),
            EndTime = endTime.ToString( "o" ),
            Success = success,
            ExitCode = exitCode,
            ErrorCategory = (int) errorCategory,
            OutputPath = AgentJobOutputWriter.GetRelativeOutputPath( jobId ),
            JobId = jobId.ToString( ),
            StepId = stepId,
        };
        if (!string.IsNullOrWhiteSpace( workflowRunId )) {
            innerRequest.WorkflowRunId = workflowRunId;
        }
        if (!string.IsNullOrWhiteSpace( outputPreview )) {
            innerRequest.OutputPreview = outputPreview;
        }
        if (scheduleId.HasValue) {
            innerRequest.ScheduleId = scheduleId.Value.ToString( );
        }
        if (!string.IsNullOrWhiteSpace( outputVariableName )) {
            innerRequest.OutputVariableName = outputVariableName;
        }
        if (!string.IsNullOrWhiteSpace( outputVariableValue )) {
            innerRequest.OutputVariableValue = outputVariableValue;
        }

        EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
            innerRequest, clientFactory.GetSharedKey(), clientFactory.GetKeyId() );

        TimeSpan delay = s_reportRetryBaseDelay;
        for (int attempt = 1; attempt <= ReportMaxRetries; attempt++) {
            try {
                Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
                EncryptedEnvelope responseEnvelope = await client.ReportJobResultAsync( requestEnvelope, callOptions );
                JobResultResponse response = PayloadEncryptor.DecryptFromEnvelope<JobResultResponse>(
                    responseEnvelope, clientFactory.GetSharedKey() );

                if (response.Accepted) {
                    if (logger.IsEnabled( LogLevel.Debug )) {
                        logger.LogDebug( "Step job result reported. Server JobId={ServerJobId}.", response.JobId );
                    }
                } else {
                    logger.LogWarning( "Server rejected step job result for task {TaskId}.", taskDef.TaskId );
                }
                return;
            } catch (Exception ex) when (attempt < ReportMaxRetries && !ct.IsCancellationRequested) {
                logger.LogWarning( ex,
                    "Failed to report step job result for task {TaskId} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}.",
                    taskDef.TaskId, attempt, ReportMaxRetries, delay );
                await Task.Delay( delay, ct );
                delay *= 2;
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to report step job result for task {TaskId} after {MaxRetries} attempts.",
                    taskDef.TaskId, ReportMaxRetries );
            }
        }
    }

    // ── Step Lifecycle Reporting ──────────────────────────────────────────────────

    /// <summary>
    /// Reports that a workflow step has started execution.
    /// </summary>
    private async Task ReportStepStartedAsync(
        Guid workflowRunId, long stepId, string stepName, long taskId, CancellationToken ct
    ) {
        try {
            JobReporting.JobReportingClient client = await clientFactory.CreateJobReportingClientAsync( ct );

            StepStartedRequest inner = new( ) {
                ConnectionId = ( await clientFactory.GetConnectionAsync( ct ) ).Id.ToString( ),
                WorkflowRunId = workflowRunId.ToString( ),
                StepId = stepId,
                StepName = stepName,
                TaskId = taskId,
                StartTime = DateTime.UtcNow.ToString( "o" ),
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                inner, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            _ = await client.ReportStepStartedAsync( envelope, callOptions );
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to report step started for step {StepId} in run {RunId}.",
                stepId, workflowRunId );
        }
    }

    /// <summary>
    /// Reports that a workflow step was skipped by control flow evaluation.
    /// </summary>
    private async Task ReportStepSkippedAsync(
        Guid workflowRunId, long stepId, string stepName, string reason, CancellationToken ct
    ) {
        try {
            JobReporting.JobReportingClient client = await clientFactory.CreateJobReportingClientAsync( ct );

            StepSkippedRequest inner = new( ) {
                ConnectionId = ( await clientFactory.GetConnectionAsync( ct ) ).Id.ToString( ),
                WorkflowRunId = workflowRunId.ToString( ),
                StepId = stepId,
                StepName = stepName,
                Reason = reason,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                inner, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            _ = await client.ReportStepSkippedAsync( envelope, callOptions );
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to report step skipped for step {StepId} in run {RunId}.",
                stepId, workflowRunId );
        }
    }

    /// <summary>
    /// Reports workflow run completion or failure to the server.
    /// Retries up to 3 times with exponential backoff on transient failure.
    /// </summary>
    private async Task CompleteWorkflowRunAsync(
        Guid workflowRunId, bool success, long? failedStepId, CancellationToken ct
    ) {
        TimeSpan delay = s_reportRetryBaseDelay;
        for (int attempt = 1; attempt <= ReportMaxRetries; attempt++) {
            try {
                VariableService.VariableServiceClient client =
                    await clientFactory.CreateVariableServiceClientAsync( ct );
                Data.Entities.Registration.RegisteredConnection connection =
                    await clientFactory.GetConnectionAsync( ct );

                CompleteWorkflowRunRequest inner = new( ) {
                    ConnectionId = connection.Id.ToString( ),
                    WorkflowRunId = workflowRunId.ToString( ),
                    Success = success,
                    EndTime = DateTime.UtcNow.ToString( "o" ),
                };

                if (failedStepId.HasValue) {
                    inner.FailedStepId = failedStepId.Value;
                }

                EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                    inner, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

                Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
                EncryptedEnvelope responseEnvelope = await client.CompleteWorkflowRunAsync( envelope, callOptions );
                CompleteWorkflowRunResponse response = PayloadEncryptor.DecryptFromEnvelope<CompleteWorkflowRunResponse>(
                    responseEnvelope, clientFactory.GetSharedKey( ) );

                if (response.Accepted) {
                    if (logger.IsEnabled( LogLevel.Information )) {
                        logger.LogInformation(
                            "Workflow run {RunId} completion reported (success={Success}).",
                            workflowRunId, success );
                    }
                } else {
                    logger.LogWarning( "Server rejected CompleteWorkflowRun for run {RunId}.", workflowRunId );
                }
                return;
            } catch (Exception ex) when (attempt < ReportMaxRetries && !ct.IsCancellationRequested) {
                logger.LogWarning( ex,
                    "Failed to report workflow run completion for {RunId} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}.",
                    workflowRunId, attempt, ReportMaxRetries, delay );
                await Task.Delay( delay, ct );
                delay *= 2;
            } catch (Exception ex) {
                logger.LogError( ex,
                    "Failed to report workflow run completion for {RunId} after {MaxRetries} attempts.",
                    workflowRunId, ReportMaxRetries );
            }
        }
    }
}
