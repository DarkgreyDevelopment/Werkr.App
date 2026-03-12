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
    IServiceScopeFactory serviceScopeFactory,
    ILogger<WorkflowExecutionService> logger
) {

    // ── Result Types ─────────────────────────────────────────────────────────────

    /// <summary>Result of executing a single workflow step.</summary>
    private sealed record StepExecutionResult(
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

        // Build topological levels from the step definitions
        IReadOnlyList<IReadOnlyList<ScheduledWorkflowStepDef>> levels =
            BuildTopologicalLevels( workflow.Steps );

        // Step result map: stepId → completed job result (concurrent for parallel steps)
        ConcurrentDictionary<long, StepJobResult> stepResults = new();

        // If/Else/ElseIf chain tracking: stepId → whether that branch was taken
        Dictionary<long, bool> branchTaken = [];

        bool workflowFailed = false;

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
                            step, workflowRunId, stepResults, branchTaken, variableCache, scheduleId, ct ) ) );

                    foreach (StepExecutionResult result in parallelResults) {
                        if (result.Job is not null) {
                            stepResults[result.StepId] = result.Job;
                        }

                        if (result.Failed) {
                            logger.LogWarning(
                                "Workflow run {RunId} failed at step {StepId}: {Error}.",
                                workflowRunId, result.StepId, result.ErrorMessage );
                            workflowFailed = true;
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
                        step, workflowRunId, stepResults, branchTaken, variableCache, scheduleId, ct);

                    if (result.Job is not null) {
                        stepResults[result.StepId] = result.Job;
                    }

                    if (result.Failed) {
                        logger.LogWarning(
                            "Workflow run {RunId} failed at step {StepId}: {Error}.",
                            workflowRunId, result.StepId, result.ErrorMessage );
                        workflowFailed = true;
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
            throw; // Propagate shutdown
        } catch (Exception ex) {
            logger.LogError( ex, "Workflow run {RunId} failed with unexpected error.", workflowRunId );
        }
    }

    // ── Step Execution ───────────────────────────────────────────────────────────

    /// <summary>Executes a single workflow step, handling control flow and variable I/O.</summary>
    private async Task<StepExecutionResult> ExecuteStepAsync(
        ScheduledWorkflowStepDef step,
        Guid workflowRunId,
        ConcurrentDictionary<long, StepJobResult> stepResults,
        Dictionary<long, bool> branchTaken,
        ConcurrentDictionary<string, string> variableCache,
        Guid? scheduleId,
        CancellationToken ct
    ) {
        ScheduledTaskDefinition? taskDef = step.Task;
        if (taskDef is null) {
            string msg = $"Step {step.StepId} has no embedded task definition.";
            return StepExecutionResult.Fail( step.StepId, msg );
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
            return StepExecutionResult.Skipped( step.StepId );
        }

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
            taskDef, workflowRunId, scheduleId, inputVariableValue, outputVariableName, ct);

        // Push output variable to server and local cache
        if (outputVariableName is not null && job.OutputVariableValue is not null) {
            variableCache[outputVariableName] = job.OutputVariableValue;
            await variableClient.PushVariableAsync( workflowRunId, outputVariableName,
                job.OutputVariableValue, step.StepId, job.JobId, ct );
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
                taskDef, workflowRunId, scheduleId, inputVariableValue, outputVariableName, ct );
            iterations++;

            // Push output variable after each iteration
            if (outputVariableName is not null && lastJob.OutputVariableValue is not null) {
                variableCache[outputVariableName] = lastJob.OutputVariableValue;
                await variableClient.PushVariableAsync( workflowRunId, outputVariableName,
                    lastJob.OutputVariableValue, step.StepId, lastJob.JobId, ct );
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

        // Report result to server with workflowRunId, schedule ID, and output variable info
        await ReportJobResultAsync(
            jobId, taskDef, startTime, endTime, success, exitCode,
            errorCategory, workflowRunId.ToString( ), tailPreview, scheduleId,
            outputVariableName, outputVariableValue, ct );

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
    private static List<IReadOnlyList<ScheduledWorkflowStepDef>> BuildTopologicalLevels(
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
            DependencyMode.All =>
                step.DependsOnStepIds.All( stepResults.ContainsKey ),
            DependencyMode.Any =>
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
}
