using Werkr.Data;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Scheduling;

/// <summary>
/// Creates one-time "Run Now" schedules that fire immediately, enabling
/// ad-hoc task and workflow execution through the existing schedule engine.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="logger">Logger.</param>
public sealed partial class RunNowService(
    WerkrDbContext dbContext,
    ILogger<RunNowService> logger
) {

    /// <summary>
    /// Creates a one-time schedule linked to the specified task and returns the schedule ID.
    /// The schedule fires immediately; the agent picks it up on the next sync/invalidation cycle.
    /// </summary>
    /// <param name="taskId">The ID of the task to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ID of the newly created one-time schedule.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the task does not exist.</exception>
    public async Task<Guid> CreateTaskRunNowAsync( long taskId, CancellationToken ct = default ) {
        WerkrTask task = await dbContext.Tasks
            .FirstOrDefaultAsync( t => t.Id == taskId, ct )
            ?? throw new KeyNotFoundException( $"Task {taskId} not found." );

        DbSchedule schedule = CreateRunNowSchedule( $"Run Now – {task.Name}" );
        _ = dbContext.Schedules.Add( schedule );
        _ = await dbContext.SaveChangesAsync( ct );

        StartDateTimeInfo startDt = CreateStartDateTimeNow( schedule.Id );
        _ = dbContext.StartDateTimeInfos.Add( startDt );

        TaskSchedule link = new( ) {
            TaskId = taskId,
            ScheduleId = schedule.Id,
            IsOneTime = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = dbContext.TaskSchedules.Add( link );
        _ = await dbContext.SaveChangesAsync( ct );

        LogRunNowCreated( logger, "task", taskId, schedule.Id );
        return schedule.Id;
    }

    /// <summary>
    /// Creates a one-time schedule linked to the specified workflow and returns the schedule ID
    /// along with the API-generated <see cref="WorkflowRun"/> ID. Seeds initial variable values
    /// (defaults and optional trigger parameters) into the run.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to run.</param>
    /// <param name="triggerVariables">Optional per-execution variable overrides from manual trigger.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of the schedule ID and the workflow run ID.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the workflow does not exist.</exception>
    public async Task<(Guid ScheduleId, Guid WorkflowRunId)> CreateWorkflowRunNowAsync(
        long workflowId,
        Dictionary<string, string>? triggerVariables = null,
        CancellationToken ct = default
    ) {
        Workflow workflow = await dbContext.Set<Workflow>( )
            .Include(w => w.Variables)
            .Include(w => w.Steps)
            .FirstOrDefaultAsync( w => w.Id == workflowId, ct )
            ?? throw new KeyNotFoundException( $"Workflow {workflowId} not found." );

        // Create WorkflowRun entity (API-side, before agent picks up)
        Guid workflowRunId = Guid.NewGuid();
        WorkflowRun run = new()
        {
            Id = workflowRunId,
            WorkflowId = workflowId,
            StartTime = DateTime.UtcNow,
            Status = WorkflowRunStatus.Running,
        };
        _ = dbContext.Set<WorkflowRun>( ).Add( run );

        // Seed default variable values
        foreach (WorkflowVariable variable in workflow.Variables) {
            if (!string.IsNullOrWhiteSpace( variable.DefaultValue )) {
                WorkflowRunVariable defaultEntry = new()
                {
                    WorkflowRunId = workflowRunId,
                    VariableName = variable.Name,
                    Value = variable.DefaultValue,
                    Version = 1,
                    Source = VariableSource.Default,
                    Created = DateTime.UtcNow,
                };
                _ = dbContext.Set<WorkflowRunVariable>( ).Add( defaultEntry );
            }
        }

        // Seed trigger parameter overrides (overwrite defaults with higher version)
        if (triggerVariables is { Count: > 0 }) {
            foreach (KeyValuePair<string, string> kvp in triggerVariables) {
                // Determine next version: if a default was seeded above, version is 2; otherwise 1
                bool hasDefault = workflow.Variables
                    .Any(v => string.Equals(v.Name, kvp.Key, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(v.DefaultValue));

                WorkflowRunVariable triggerEntry = new()
                {
                    WorkflowRunId = workflowRunId,
                    VariableName = kvp.Key,
                    Value = kvp.Value,
                    Version = hasDefault ? 2 : 1,
                    Source = VariableSource.ManualInput,
                    Created = DateTime.UtcNow,
                };
                _ = dbContext.Set<WorkflowRunVariable>( ).Add( triggerEntry );
            }
        }

        _ = await dbContext.SaveChangesAsync( ct );

        // Validate input variables are covered by defaults, trigger params, or upstream step outputs
        ValidateInputVariableCoverage( workflow, triggerVariables );

        // Create the one-time schedule
        DbSchedule schedule = CreateRunNowSchedule( $"Run Now – {workflow.Name}" );
        _ = dbContext.Schedules.Add( schedule );
        _ = await dbContext.SaveChangesAsync( ct );

        StartDateTimeInfo startDt = CreateStartDateTimeNow( schedule.Id );
        _ = dbContext.StartDateTimeInfos.Add( startDt );

        WorkflowSchedule link = new( ) {
            WorkflowId = workflowId,
            ScheduleId = schedule.Id,
            IsOneTime = true,
            CreatedAtUtc = DateTime.UtcNow,
            WorkflowRunId = workflowRunId,
        };
        _ = dbContext.WorkflowSchedules.Add( link );
        _ = await dbContext.SaveChangesAsync( ct );

        LogRunNowCreated( logger, "workflow", workflowId, schedule.Id );
        return (schedule.Id, workflowRunId);
    }

    /// <summary>
    /// Creates an ephemeral task, links it to a one-time schedule, and returns
    /// both the task ID and schedule ID.  Ephemeral tasks are invisible in
    /// the normal task list and are intended for single ad-hoc executions
    /// (e.g. console shell commands).
    /// </summary>
    /// <param name="command">The shell command or script content to execute.</param>
    /// <param name="actionType">The action type (e.g. <see cref="TaskActionType.ShellCommand"/>).</param>
    /// <param name="targetTags">Optional target tags used to route the task to specific agents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of the new task ID and schedule ID.</returns>
    public async Task<(long TaskId, Guid ScheduleId)> CreateEphemeralTaskAsync(
        string command,
        TaskActionType actionType,
        string[]? targetTags = null,
        CancellationToken ct = default
    ) {
        WerkrTask task = new( ) {
            Name = $"Ephemeral – {DateTime.UtcNow:u}",
            Description = "Ephemeral task created for ad-hoc execution.",
            ActionType = actionType,
            Content = command,
            Enabled = true,
            IsEphemeral = true,
            TargetTags = targetTags ?? [],
            SyncIntervalMinutes = 60,
            TimeoutMinutes = 60,
        };
        _ = dbContext.Tasks.Add( task );
        _ = await dbContext.SaveChangesAsync( ct );

        DbSchedule schedule = CreateRunNowSchedule( $"Ephemeral – task {task.Id}" );
        _ = dbContext.Schedules.Add( schedule );
        _ = await dbContext.SaveChangesAsync( ct );

        StartDateTimeInfo startDt = CreateStartDateTimeNow( schedule.Id );
        _ = dbContext.StartDateTimeInfos.Add( startDt );

        TaskSchedule link = new( ) {
            TaskId = task.Id,
            ScheduleId = schedule.Id,
            IsOneTime = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _ = dbContext.TaskSchedules.Add( link );
        _ = await dbContext.SaveChangesAsync( ct );

        LogEphemeralCreated( logger, task.Id, schedule.Id );
        return (task.Id, schedule.Id);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal <see cref="DbSchedule"/> configured for immediate one-time execution.
    /// </summary>
    private static DbSchedule CreateRunNowSchedule( string name ) => new( ) {
        Name = name,
        StopTaskAfterMinutes = 60,
        CatchUpEnabled = true,
    };

    /// <summary>
    /// Builds a <see cref="StartDateTimeInfo"/> pointing to "now" in UTC.
    /// </summary>
    private static StartDateTimeInfo CreateStartDateTimeNow( Guid scheduleId ) {
        DateTime utcNow = DateTime.UtcNow;
        return new StartDateTimeInfo {
            ScheduleId = scheduleId,
            Date = DateOnly.FromDateTime( utcNow ),
            Time = TimeOnly.FromDateTime( utcNow ),
            TimeZone = TimeZoneInfo.Utc,
        };
    }

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Created Run-Now schedule {ScheduleId} for {EntityType} {EntityId}." )]
    private static partial void LogRunNowCreated(
        ILogger logger, string entityType, long entityId, Guid scheduleId );

    [LoggerMessage( Level = LogLevel.Information,
        Message = "Created ephemeral task {TaskId} with schedule {ScheduleId}." )]
    private static partial void LogEphemeralCreated(
        ILogger logger, long taskId, Guid scheduleId );

    /// <summary>
    /// Validates that every <see cref="WorkflowStep.InputVariableName"/> is covered by either
    /// a default value, a trigger parameter, or an upstream step's output variable.
    /// Logs a warning for each uncovered input — does not throw.
    /// </summary>
    /// <param name="workflow">The workflow with Steps and Variables loaded.</param>
    /// <param name="triggerVariables">Optional trigger parameter overrides.</param>
    private void ValidateInputVariableCoverage(
        Workflow workflow,
        Dictionary<string, string>? triggerVariables
    ) {
        if (workflow.Steps is not { Count: > 0 }) {
            return;
        }

        // Names that have a default value
        HashSet<string> defaults = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowVariable v in workflow.Variables) {
            if (!string.IsNullOrWhiteSpace( v.DefaultValue )) {
                _ = defaults.Add( v.Name );
            }
        }

        // Names provided as trigger params
        HashSet<string> triggers = triggerVariables is { Count: > 0 }
            ? new(triggerVariables.Keys, StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);

        // Names produced by some step's output
        HashSet<string> produced = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowStep step in workflow.Steps) {
            if (!string.IsNullOrWhiteSpace( step.OutputVariableName )) {
                _ = produced.Add( step.OutputVariableName );
            }
        }

        foreach (WorkflowStep step in workflow.Steps) {
            if (string.IsNullOrWhiteSpace( step.InputVariableName )) {
                continue;
            }

            string name = step.InputVariableName;
            if (!defaults.Contains( name ) && !triggers.Contains( name ) && !produced.Contains( name )) {
                LogUncoveredInputVariable( logger, step.Id, step.Order, name, workflow.Id );
            }
        }
    }

    [LoggerMessage( Level = LogLevel.Warning,
        Message = "Workflow {WorkflowId} step {StepId} (order {StepOrder}) declares input variable '{VariableName}' " +
                  "which has no default value, trigger parameter, or upstream step output. " +
                  "The step will receive null at runtime." )]
    private static partial void LogUncoveredInputVariable(
        ILogger logger, long stepId, int stepOrder, string variableName, long workflowId );
}
