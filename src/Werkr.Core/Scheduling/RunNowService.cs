using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    /// Creates a one-time schedule linked to the specified workflow and returns the schedule ID.
    /// The schedule fires immediately; the agent picks it up on the next sync/invalidation cycle.
    /// </summary>
    /// <param name="workflowId">The ID of the workflow to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ID of the newly created one-time schedule.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the workflow does not exist.</exception>
    public async Task<Guid> CreateWorkflowRunNowAsync( long workflowId, CancellationToken ct = default ) {
        Workflow workflow = await dbContext.Set<Workflow>( )
            .FirstOrDefaultAsync( w => w.Id == workflowId, ct )
            ?? throw new KeyNotFoundException( $"Workflow {workflowId} not found." );

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
        };
        _ = dbContext.WorkflowSchedules.Add( link );
        _ = await dbContext.SaveChangesAsync( ct );

        LogRunNowCreated( logger, "workflow", workflowId, schedule.Id );
        return schedule.Id;
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
}
