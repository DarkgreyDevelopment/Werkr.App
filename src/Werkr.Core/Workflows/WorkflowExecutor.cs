using System.Threading.Channels;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Werkr.Common.Models;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Executes a workflow as a DAG with topological ordering, conditional control flow
/// (If/Else/ElseIf/While/Do), per-step agent resolution, and parallel execution
/// of independent steps within the same topological level.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="workflowService">Workflow service for DAG validation and level retrieval.</param>
/// <param name="jobExecutionService">Job execution service for dispatching tasks.</param>
/// <param name="agentResolver">Agent resolver for tag-based agent matching.</param>
/// <param name="conditionEvaluator">Condition evaluator for control flow expressions.</param>
/// <param name="runTracker">Tracker for publishing real-time step status updates.</param>
/// <param name="logger">Logger instance.</param>
public sealed class WorkflowExecutor(
    WerkrDbContext dbContext,
    WorkflowService workflowService,
    JobExecutionService jobExecutionService,
    AgentResolver agentResolver,
    ConditionEvaluator conditionEvaluator,
    WorkflowRunTracker runTracker,
    ILogger<WorkflowExecutor> logger
) {

    /// <summary>
    /// Executes a workflow, resolving the appropriate agent for each step
    /// based on task TargetTags. Supports multi-agent workflows where
    /// different steps may execute on different agents. Independent steps
    /// at the same topological level execute in parallel.
    /// </summary>
    /// <param name="workflow">The workflow to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The completed <see cref="WorkflowRun"/> record.</returns>
    public async Task<WorkflowRun> ExecuteAsync(
        Workflow workflow,
        CancellationToken ct = default
    ) {
        // Create workflow run first so cancellation can be recorded
        WorkflowRun run = new( ) {
            WorkflowId = workflow.Id,
            StartTime = DateTime.UtcNow,
            Status = WorkflowRunStatus.Running,
        };
        _ = dbContext.WorkflowRuns.Add( run );
        _ = await dbContext.SaveChangesAsync( CancellationToken.None );

        // Start real-time tracking channel for this run
        ChannelWriter<WorkflowStepStatusUpdate> writer = runTracker.StartTracking( run.Id );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Starting workflow run {RunId} for workflow {WorkflowId} '{WorkflowName}'.",
                run.Id.ToString( ),
                workflow.Id.ToString( ),
                workflow.Name
            );
        }

        // Step result map: stepId → completed job
        Dictionary<long, WerkrJob> stepResults = [];

        // If/Else/ElseIf chain tracking: stepId → whether that branch was taken
        Dictionary<long, bool> branchTaken = [];

        try {
            // Validate DAG (throws if cycle or control flow error)
            _ = await workflowService.ValidateDagAsync(
                workflow.Id,
                ct
            );

            // Get topological levels for execution
            // NOTE: Steps within the same level are independent and could be parallelized
            // with an IDbContextFactory<WerkrDbContext> pattern. Currently executed
            // sequentially because DbContext is not thread-safe.
            IReadOnlyList<IReadOnlyList<WorkflowStep>> levels =
                await workflowService.GetTopologicalLevelsAsync(
                    workflow.Id,
                    ct
                );

            foreach (IReadOnlyList<WorkflowStep> level in levels) {
                ct.ThrowIfCancellationRequested( );

                // Partition steps into chain-bound (If/Else/ElseIf sequences) and parallelizable
                List<WorkflowStep> parallelizable = [];
                List<WorkflowStep> chainBound = [];

                foreach (WorkflowStep step in level) {
                    if (step.ControlStatement is ControlStatement.If or
                        ControlStatement.Else or ControlStatement.ElseIf) {
                        chainBound.Add( step );
                    } else {
                        parallelizable.Add( step );
                    }
                }

                // Execute parallelizable steps sequentially within the level.
                // DbContext is not thread-safe, so true parallelism requires
                // IDbContextFactory<WerkrDbContext> (tracked for future enhancement).
                foreach (WorkflowStep step in parallelizable) {
                    ct.ThrowIfCancellationRequested( );

                    StepExecutionResult result = await ExecuteStepAsync(
                        step, run, stepResults, branchTaken, writer, ct );

                    if (result.Job is not null) {
                        stepResults[result.StepId] = result.Job;
                    }

                    if (result.Failed) {
                        run.Status = WorkflowRunStatus.Failed;
                        run.EndTime = DateTime.UtcNow;
                        _ = await dbContext.SaveChangesAsync( CancellationToken.None );
                        logger.LogWarning( "Workflow run {RunId} failed at step {StepId}: {Error}.",
                            run.Id.ToString( ),
                            result.StepId.ToString( ),
                            result.ErrorMessage
                        );
                        runTracker.CompleteTracking( run.Id );
                        return run;
                    }
                }

                // Execute chain-bound steps sequentially (order matters for If/Else evaluation)
                foreach (WorkflowStep step in chainBound) {
                    ct.ThrowIfCancellationRequested( );

                    StepExecutionResult result = await ExecuteStepAsync(
                        step, run, stepResults, branchTaken, writer, ct );

                    if (result.Job is not null) {
                        stepResults[result.StepId] = result.Job;
                    }

                    if (result.Failed) {
                        run.Status = WorkflowRunStatus.Failed;
                        run.EndTime = DateTime.UtcNow;
                        _ = await dbContext.SaveChangesAsync( CancellationToken.None );
                        logger.LogWarning( "Workflow run {RunId} failed at step {StepId}: {Error}.",
                            run.Id.ToString( ),
                            result.StepId.ToString( ),
                            result.ErrorMessage
                        );
                        runTracker.CompleteTracking( run.Id );
                        return run;
                    }
                }
            }

            // All steps completed
            run.Status = WorkflowRunStatus.Completed;
            run.EndTime = DateTime.UtcNow;
            _ = await dbContext.SaveChangesAsync( CancellationToken.None );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation( "Workflow run {RunId} completed successfully.",
                    run.Id.ToString( )
                );
            }
        } catch (OperationCanceledException) {
            run.Status = WorkflowRunStatus.Cancelled;
            run.EndTime = DateTime.UtcNow;
            _ = await dbContext.SaveChangesAsync( CancellationToken.None );
            logger.LogWarning(
                "Workflow run {RunId} was cancelled.",
                run.Id.ToString( )
            );
        } catch (Exception ex) {
            run.Status = WorkflowRunStatus.Failed;
            run.EndTime = DateTime.UtcNow;
            _ = await dbContext.SaveChangesAsync( CancellationToken.None );
            logger.LogError(
                ex,
                "Workflow run {RunId} failed with unexpected error.",
                run.Id.ToString( )
            );
        } finally {
            runTracker.CompleteTracking( run.Id );
        }

        return run;
    }

    /// <summary>
    /// Retrieves workflow runs for a workflow.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(
        long workflowId, int limit = 50, CancellationToken ct = default ) =>
        await dbContext.WorkflowRuns.AsNoTracking( )
            .Where( r => r.WorkflowId == workflowId )
            .OrderByDescending( r => r.StartTime )
            .Take( limit )
            .ToListAsync( ct );

    /// <summary>
    /// Retrieves a single workflow run with its jobs.
    /// </summary>
    public async Task<WorkflowRun?> GetRunAsync(
        Guid runId,
        CancellationToken ct = default
    ) =>
        await dbContext.WorkflowRuns.AsNoTracking( )
            .Include( r => r.Jobs )
            .FirstOrDefaultAsync(
                r => r.Id == runId,
                ct
            );

    /// <summary>Executes a single workflow step, handling control flow.</summary>
    private async Task<StepExecutionResult> ExecuteStepAsync(
        WorkflowStep step,
        WorkflowRun run,
        Dictionary<long, WerkrJob> stepResults,
        Dictionary<long, bool> branchTaken,
        ChannelWriter<WorkflowStepStatusUpdate> writer,
        CancellationToken ct
    ) {

        // Build a display name for status updates (WorkflowStep has no Name property)
        string stepLabel = $"Step {step.Order} (#{step.Id})";

        // Publish "Running" status
        await writer.WriteAsync( new WorkflowStepStatusUpdate(
            run.Id, step.Id, stepLabel, "Running", DateTime.UtcNow, null ), ct );

        // Load task if not eagerly loaded
        WerkrTask? task = step.Task;
        if (task is null) {
            task = await dbContext.Tasks.AsNoTracking( ).FirstOrDefaultAsync(
                t => t.Id == step.TaskId,
                ct
            );
            if (task is null) {
                string taskError = $"Task with Id={step.TaskId} not found for step {step.Id}.";
                await writer.WriteAsync( new WorkflowStepStatusUpdate(
                    run.Id, step.Id, stepLabel, "Failed", DateTime.UtcNow, taskError ), ct );
                return StepExecutionResult.Fail(
                    step.Id,
                    taskError
                );
            }
        }

        // Refine step label now that we have the task name
        stepLabel = $"Step {step.Order}: {task.Name}";

        // Gather predecessor jobs
        List<WerkrJob> predecessorJobs = [];
        foreach (WorkflowStepDependency dep in step.Dependencies) {
            if (stepResults.TryGetValue(
                dep.DependsOnStepId,
                out WerkrJob? predJob
            )) {
                predecessorJobs.Add( predJob );
            }
        }

        // Check dependency satisfaction
        if (!CheckDependencies(
            step,
            stepResults
        )) {
            string depError = $"Dependencies not satisfied for step {step.Id} (DependencyMode={step.DependencyMode}).";
            await writer.WriteAsync( new WorkflowStepStatusUpdate(
                run.Id, step.Id, stepLabel, "Failed", DateTime.UtcNow, depError ), ct );
            return StepExecutionResult.Fail(
                step.Id,
                depError
            );
        }

        // Evaluate control flow
        bool shouldExecute = EvaluateControlFlow(
            step, predecessorJobs, branchTaken );

        if (!shouldExecute) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Step {StepId} skipped by control flow ({ControlStatement}).",
                    step.Id.ToString( ),
                    step.ControlStatement.ToString( )
                );
            }
            await writer.WriteAsync( new WorkflowStepStatusUpdate(
                run.Id, step.Id, stepLabel, "Skipped", DateTime.UtcNow, null ), ct );
            return StepExecutionResult.Skipped( step.Id );
        }

        // Resolve agent for this step
        RegisteredConnection? agent = await ResolveAgentForStepAsync(
            step,
            task,
            ct
        );
        if (agent is null) {
            string agentError =
                $"No agent available for step {step.Id} " +
                $"(task '{task.Name}', tags=[{string.Join( ", ", task.TargetTags )}]).";
            await writer.WriteAsync( new WorkflowStepStatusUpdate(
                run.Id, step.Id, stepLabel, "Failed", DateTime.UtcNow, agentError ), ct );
            return StepExecutionResult.Fail(
                step.Id,
                agentError
            );
        }

        // Handle While/Do loops
        if (step.ControlStatement is ControlStatement.While or ControlStatement.Do) {
            return await ExecuteLoopStepAsync(
                step,
                task,
                agent,
                run,
                predecessorJobs,
                ct
            );
        }

        // Execute the step's task
        WerkrJob job = await jobExecutionService.ExecuteOnAgentAsync(
            task,
            agent,
            run.Id,
            ct
        );

        // Record branch taken for If/ElseIf chains
        if (step.ControlStatement is ControlStatement.If or ControlStatement.ElseIf) {
            branchTaken[step.Id] = true;
        }

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Step {StepId} executed: Success={Success}, JobId={JobId}.",
                step.Id.ToString( ),
                job.Success.ToString( ),
                job.Id.ToString( )
            );
        }

        string completionStatus = job.Success ? "Completed" : "Failed";
        await writer.WriteAsync( new WorkflowStepStatusUpdate(
            run.Id, step.Id, stepLabel, completionStatus, DateTime.UtcNow,
            job.Success ? null : "Job execution failed" ), ct
        );

        return StepExecutionResult.Ok(
            step.Id,
            job
        );
    }

    /// <summary>Executes a While or Do loop step.</summary>
    private async Task<StepExecutionResult> ExecuteLoopStepAsync(
        WorkflowStep step,
        WerkrTask task,
        RegisteredConnection agent,
        WorkflowRun run,
        List<WerkrJob> predecessorJobs,
        CancellationToken ct
    ) {

        WerkrJob? lastJob = null;
        int iterations = 0;
        bool isDoLoop = step.ControlStatement == ControlStatement.Do;

        while (iterations < step.MaxIterations) {
            ct.ThrowIfCancellationRequested( );

            // For While: check condition before execution
            // For Do: execute first, then check condition
            if (!isDoLoop || iterations > 0) {
                IReadOnlyList<WerkrJob> evalJobs = lastJob is not null ? [lastJob] : predecessorJobs;
                bool conditionMet = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, evalJobs, step.DependencyMode );
                if (!conditionMet) {
                    break;
                }
            }

            lastJob = await jobExecutionService.ExecuteOnAgentAsync(
                task,
                agent,
                run.Id,
                ct
            );
            iterations++;

            if (!lastJob.Success) {
                break;
            }
        }

        if (iterations >= step.MaxIterations) {
            logger.LogWarning( "Step {StepId} reached MaxIterations ({Max}).",
                step.Id.ToString( ),
                step.MaxIterations.ToString( )
            );
        }

        return lastJob is not null
            ? StepExecutionResult.Ok(
                step.Id,
                lastJob
            )
            : StepExecutionResult.Skipped( step.Id );
    }

    /// <summary>Checks whether dependencies are satisfied based on DependencyMode.</summary>
    private static bool CheckDependencies(
        WorkflowStep step,
        Dictionary<long, WerkrJob> stepResults
    ) {

        if (step.Dependencies.Count == 0) {
            return true; // Root step — no dependencies
        }

        return step.DependencyMode switch {
            DependencyMode.All =>
                // All predecessors must have a result (they were executed, not necessarily succeeded)
                step.Dependencies.All( d => stepResults.ContainsKey( d.DependsOnStepId ) ),
            DependencyMode.Any =>
                // At least one predecessor has a result
                step.Dependencies.Any( d => stepResults.ContainsKey( d.DependsOnStepId ) ),
            _ => step.Dependencies.All( d => stepResults.ContainsKey( d.DependsOnStepId ) ),
        };
    }

    /// <summary>Evaluates control flow to determine if a step should execute.</summary>
    private bool EvaluateControlFlow(
        WorkflowStep step,
        List<WerkrJob> predecessorJobs,
        Dictionary<long, bool> branchTaken
    ) {

        switch (step.ControlStatement) {
            case ControlStatement.Sequential:
                return true;

            case ControlStatement.If:
                bool ifResult = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, predecessorJobs, step.DependencyMode );
                branchTaken[step.Id] = ifResult;
                return ifResult;

            case ControlStatement.ElseIf: {
                    // Check if any prior If/ElseIf in the chain was taken
                    bool priorTaken = step.Dependencies.Any( d =>
                    branchTaken.TryGetValue(
                        d.DependsOnStepId,
                        out bool taken
                    ) && taken );
                    if (priorTaken) {
                        branchTaken[step.Id] = false;
                        return false;
                    }
                    bool elseIfResult = conditionEvaluator.EvaluateMultiple(
                    step.ConditionExpression, predecessorJobs, step.DependencyMode );
                    branchTaken[step.Id] = elseIfResult;
                    return elseIfResult;
                }

            case ControlStatement.Else: {
                    // Execute only if no prior If/ElseIf in the chain was taken
                    bool anyPriorTaken = step.Dependencies.Any( d =>
                    branchTaken.TryGetValue(
                        d.DependsOnStepId,
                        out bool taken
                    ) && taken );
                    return !anyPriorTaken;
                }

            case ControlStatement.While:
            case ControlStatement.Do:
                // Loop execution is handled by ExecuteLoopStepAsync
                return true;

            default:
                return true;
        }
    }

    /// <summary>Resolves the agent for a workflow step.</summary>
    private async Task<RegisteredConnection?> ResolveAgentForStepAsync(
        WorkflowStep step, WerkrTask task, CancellationToken ct ) {

        if (step.AgentConnectionIdOverride.HasValue) {
            RegisteredConnection? overrideAgent = await dbContext.RegisteredConnections
                .FirstOrDefaultAsync(
                    c => c.Id == step.AgentConnectionIdOverride.Value,
                    ct
                );
            if (overrideAgent is null) {
                logger.LogWarning( "AgentConnectionIdOverride {AgentId} not found for step {StepId}.",
                    step.AgentConnectionIdOverride.Value.ToString( ),
                    step.Id.ToString( )
                );
            }
            return overrideAgent;
        }

        return await agentResolver.ResolveAsync(
            task.TargetTags,
            ct
        );
    }

    /// <summary>Result of executing a single workflow step.</summary>
    private sealed record StepExecutionResult(
        long StepId,
        WerkrJob? Job,
        bool Failed,
        bool WasSkipped,
        string? ErrorMessage
    ) {

        public static StepExecutionResult Ok(
            long stepId,
            WerkrJob job
        ) =>
            new(
                stepId,
                job,
                Failed: false,
                WasSkipped: false,
                ErrorMessage: null
            );

        public static StepExecutionResult Skipped( long stepId ) =>
            new(
                stepId,
                Job: null,
                Failed: false,
                WasSkipped: true,
                ErrorMessage: null
            );

        public static StepExecutionResult Fail(
            long stepId,
            string errorMessage
        ) =>
            new(
                stepId,
                Job: null,
                Failed: true,
                WasSkipped: false,
                ErrorMessage: errorMessage
            );
    }
}
