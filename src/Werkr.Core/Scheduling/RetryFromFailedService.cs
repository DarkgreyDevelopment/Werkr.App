using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Data;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Orchestrates retry-from-failed for a workflow run. Atomically resets the
/// target step and all downstream steps, applies variable overrides, and
/// creates a one-time schedule for re-execution.
/// </summary>
public sealed partial class RetryFromFailedService(
    WerkrDbContext dbContext,
    ILogger<RetryFromFailedService> logger
) {

    /// <summary>Result returned by <see cref="RetryAsync"/>.</summary>
    public sealed record RetryResult( Guid RunId, long RetryFromStepId, int ResetStepCount, Guid ScheduleId );

    /// <summary>
    /// Retries a failed workflow run starting from the specified step.
    /// The target step and all transitive downstream steps are reset to Pending.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="runId">The run to retry.</param>
    /// <param name="stepId">The failed step to retry from.</param>
    /// <param name="variableOverrides">Optional variable value overrides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="RetryResult"/> on success.</returns>
    /// <exception cref="InvalidOperationException">If the run is not in Failed status or the step is not failed.</exception>
    /// <exception cref="KeyNotFoundException">If the step doesn't exist or doesn't belong to the workflow.</exception>
    public async Task<RetryResult> RetryAsync(
        long workflowId, Guid runId, long stepId,
        Dictionary<string, string>? variableOverrides,
        CancellationToken ct = default ) {

        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync( ct );

        // Step 1: Atomic status transition via compare-and-swap
        int rowsAffected = await dbContext.WorkflowRuns
            .Where(r => r.Id == runId && r.WorkflowId == workflowId && r.Status == WorkflowRunStatus.Failed)
            .ExecuteUpdateAsync( s => s
                .SetProperty( r => r.Status, WorkflowRunStatus.Running )
                .SetProperty( r => r.EndTime, (DateTime?)null )
                .SetProperty( r => r.LastUpdated, DateTime.UtcNow ),
                ct );

        if (rowsAffected == 0) {
            throw new InvalidOperationException( "Run is not in Failed status or does not belong to this workflow. Cannot retry." );
        }

        // Step 2: Validate step belongs to workflow
        WorkflowStep step = await dbContext.WorkflowSteps
            .FirstOrDefaultAsync( s => s.Id == stepId && s.WorkflowId == workflowId, ct )
            ?? throw new KeyNotFoundException( $"Step {stepId} not found in workflow {workflowId}." );

        // Step 3: Validate target step has a Failed execution
        bool hasFailed = await dbContext.WorkflowStepExecutions
            .Where( e => e.WorkflowRunId == runId && e.StepId == stepId && e.Status == StepExecutionStatus.Failed )
            .AnyAsync(ct);
        if (!hasFailed) {
            throw new InvalidOperationException( $"Step {stepId} does not have a Failed execution in this run." );
        }

        // Step 4: Compute downstream steps (transitive dependents from the target step)
        HashSet<long> stepsToReset = await ComputeDownstreamStepsAsync( workflowId, stepId, ct );
        _ = stepsToReset.Add( stepId ); // Include the target step itself

        // Step 5: Reset step executions — insert new rows with incremented attempt
        Dictionary<long, int> maxAttempts = await dbContext.WorkflowStepExecutions
            .Where(e => e.WorkflowRunId == runId && stepsToReset.Contains(e.StepId))
            .GroupBy(e => e.StepId)
            .Select(g => new { StepId = g.Key, Max = g.Max(e => e.Attempt) })
            .ToDictionaryAsync(x => x.StepId, x => x.Max, ct);

        foreach (long resetStepId in stepsToReset) {
            _ = maxAttempts.TryGetValue( resetStepId, out int maxAttempt );

            WorkflowStepExecution pendingExecution = new( ) {
                WorkflowRunId = runId,
                StepId = resetStepId,
                Attempt = maxAttempt + 1,
                Status = StepExecutionStatus.Pending,
            };
            _ = dbContext.WorkflowStepExecutions.Add( pendingExecution );
        }

        // Step 6: Apply variable overrides
        if (variableOverrides is { Count: > 0 }) {
            List<string> variableNames = [.. variableOverrides.Keys];

            Dictionary<string, int> maxVersions = await dbContext.Set<WorkflowRunVariable>()
                .Where(v => v.WorkflowRunId == runId && variableNames.Contains(v.VariableName))
                .GroupBy(v => v.VariableName)
                .Select(g => new { VariableName = g.Key, MaxVersion = g.Max(v => v.Version) })
                .ToDictionaryAsync(x => x.VariableName, x => x.MaxVersion, ct);

            foreach (KeyValuePair<string, string> kvp in variableOverrides) {
                _ = maxVersions.TryGetValue( kvp.Key, out int maxVersion );

                WorkflowRunVariable overrideVar = new( ) {
                    WorkflowRunId = runId,
                    VariableName = kvp.Key,
                    Value = kvp.Value,
                    Version = maxVersion + 1,
                    Source = VariableSource.ReExecutionEdit,
                    Created = DateTime.UtcNow,
                };
                _ = dbContext.Set<WorkflowRunVariable>( ).Add( overrideVar );
            }
        }

        _ = await dbContext.SaveChangesAsync( ct );

        // Step 7: Create one-time schedule (same pattern as RunNowService)
        DbSchedule schedule = new( ) {
            Name = $"Retry from Step {stepId} – Run {runId}",
            StopTaskAfterMinutes = 60,
            CatchUpEnabled = true,
        };
        _ = dbContext.Schedules.Add( schedule );
        _ = await dbContext.SaveChangesAsync( ct );

        DateTime utcNow = DateTime.UtcNow;
        StartDateTimeInfo startDt = new( ) {
            ScheduleId = schedule.Id,
            Date = DateOnly.FromDateTime( utcNow ),
            Time = TimeOnly.FromDateTime( utcNow ),
            TimeZone = TimeZoneInfo.Utc,
        };
        _ = dbContext.StartDateTimeInfos.Add( startDt );

        WorkflowSchedule link = new( ) {
            WorkflowId = workflowId,
            ScheduleId = schedule.Id,
            IsOneTime = true,
            CreatedAtUtc = DateTime.UtcNow,
            WorkflowRunId = runId,
        };
        _ = dbContext.WorkflowSchedules.Add( link );
        _ = await dbContext.SaveChangesAsync( ct );

        await transaction.CommitAsync( ct );

        LogRetryCreated( logger, runId, stepId, stepsToReset.Count, schedule.Id );
        return new RetryResult( runId, stepId, stepsToReset.Count, schedule.Id );
    }

    /// <summary>
    /// Computes all steps transitively downstream from the given step in the DAG.
    /// </summary>
    private async Task<HashSet<long>> ComputeDownstreamStepsAsync(
        long workflowId, long fromStepId, CancellationToken ct ) {

        // Build adjacency: step → list of dependents (steps that depend on it)
        List<WorkflowStepDependency> allDeps = await dbContext.Set<WorkflowStepDependency>( )
            .Where( d => d.Step!.WorkflowId == workflowId )
            .ToListAsync( ct );

        Dictionary<long, List<long>> adjacency = [];
        foreach (WorkflowStepDependency dep in allDeps) {
            if (!adjacency.TryGetValue( dep.DependsOnStepId, out List<long>? dependents )) {
                dependents = [];
                adjacency[dep.DependsOnStepId] = dependents;
            }
            dependents.Add( dep.StepId );
        }

        // BFS from fromStepId to find all transitive dependents
        HashSet<long> downstream = [];
        Queue<long> queue = new( );
        if (adjacency.TryGetValue( fromStepId, out List<long>? initial )) {
            foreach (long s in initial) {
                queue.Enqueue( s );
            }
        }

        while (queue.Count > 0) {
            long current = queue.Dequeue( );
            if (!downstream.Add( current )) {
                continue;
            }

            if (adjacency.TryGetValue( current, out List<long>? next )) {
                foreach (long s in next) {
                    if (!downstream.Contains( s )) {
                        queue.Enqueue( s );
                    }
                }
            }
        }

        return downstream;
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Retry created for run {RunId} from step {StepId}: {ResetCount} steps reset, schedule {ScheduleId}." )]
    private static partial void LogRetryCreated( ILogger logger, Guid runId, long stepId, int resetCount, Guid scheduleId );
}
