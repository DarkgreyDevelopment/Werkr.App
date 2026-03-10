using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for Agent schedule synchronization.
/// Agents call <see cref="GetAssignedSchedules"/> to pull tasks and workflows
/// whose <c>TargetTags</c> intersect with the agent's tags.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="scheduleService">Schedule service for loading composite schedules.</param>
/// <param name="holidayDateService">Holiday date materialization service.</param>
/// <param name="holidayCalendarService">Holiday calendar CRUD service.</param>
/// <param name="logger">Logger instance.</param>
public sealed class ScheduleSyncGrpcService(
    WerkrDbContext dbContext,
    ScheduleService scheduleService,
    HolidayDateService holidayDateService,
    HolidayCalendarService holidayCalendarService,
    ILogger<ScheduleSyncGrpcService> logger
) : ScheduleSync.ScheduleSyncBase {

    /// <summary>
    /// Returns all enabled tasks and workflows with schedules whose task TargetTags
    /// intersect with the requesting agent's tags.
    /// </summary>
    public override async Task<EncryptedEnvelope> GetAssignedSchedules(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        AgentScheduleRequest inner = PayloadEncryptor.DecryptFromEnvelope<AgentScheduleRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        // Use the server-authoritative tags from the resolved connection (set by admin)
        // rather than the agent-reported tags, which may be stale or empty.
        HashSet<string> agentTags = new( connection.Tags ?? [], StringComparer.OrdinalIgnoreCase );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "Agent {AgentId} requesting schedules with tags [{Tags}] (agent-reported: [{AgentReportedTags}]).",
                inner.ConnectionId, string.Join( ", ", agentTags ), string.Join( ", ", inner.Tags ) );
        }

        AgentScheduleResponse response = new( );

        // ── Standalone tasks with schedules ──
        List<TaskSchedule> taskSchedules = await dbContext.TaskSchedules
            .AsNoTracking( )
            .Include( ts => ts.Task )
            .Include( ts => ts.Schedule )
            .Where( ts => ts.Task!.Enabled )
            .ToListAsync( context.CancellationToken );

        foreach (TaskSchedule ts in taskSchedules) {
            WerkrTask task = ts.Task!;
            // Tag intersection (in-memory for JSON column compatibility)
            if (!task.TargetTags.Any( tag => agentTags.Contains( tag.Trim( ) ) )) {
                continue;
            }

            Schedule? schedule = await scheduleService.GetByIdAsync( ts.ScheduleId, context.CancellationToken );
            if (schedule is null) {
                continue;
            }

            ScheduledTaskDefinition taskDef = MapTaskDefinition( task, schedule );
            response.Tasks.Add( taskDef );
        }

        // ── Workflows with schedules ──
        List<WorkflowSchedule> workflowSchedules = await dbContext.WorkflowSchedules
            .AsNoTracking( )
            .Include( ws => ws.Workflow )
                .ThenInclude( w => w!.Steps )
                    .ThenInclude( s => s.Task )
            .Include( ws => ws.Workflow )
                .ThenInclude( w => w!.Steps )
                    .ThenInclude( s => s.Dependencies )
            .Include(ws => ws.Workflow)
                .ThenInclude(w => w!.Variables)
            .Where( ws => ws.Workflow!.Enabled )
            .ToListAsync( context.CancellationToken );

        foreach (WorkflowSchedule ws in workflowSchedules) {
            Workflow workflow = ws.Workflow!;
            // Check if any task in the workflow matches the agent's tags
            bool anyMatch = workflow.Steps.Any( step =>
                step.Task is not null &&
                step.Task.TargetTags.Any( tag => agentTags.Contains( tag.Trim( ) ) ) );
            if (!anyMatch) {
                continue;
            }

            Schedule? schedule = await scheduleService.GetByIdAsync( ws.ScheduleId, context.CancellationToken );
            if (schedule is null) {
                continue;
            }

            ScheduledWorkflowDefinition workflowDef = MapWorkflowDefinition( workflow, schedule );

            // For run-now schedules, include the API-generated workflow run ID
            if (ws.WorkflowRunId.HasValue) {
                workflowDef.WorkflowRunId = ws.WorkflowRunId.Value.ToString( );

                // Include trigger variables (ManualInput entries) for local cache seeding
                List<WorkflowRunVariable> triggerVars = await dbContext.Set<WorkflowRunVariable>()
                    .AsNoTracking()
                    .Where(v => v.WorkflowRunId == ws.WorkflowRunId.Value
                        && v.Source == VariableSource.ManualInput)
                    .ToListAsync(context.CancellationToken);

                foreach (WorkflowRunVariable tv in triggerVars) {
                    workflowDef.TriggerVariables[tv.VariableName] = tv.Value;
                }
            }

            response.Workflows.Add( workflowDef );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Returning {TaskCount} tasks and {WorkflowCount} workflows for agent {AgentId}.",
                response.Tasks.Count.ToString( ), response.Workflows.Count.ToString( ), inner.ConnectionId );
        }

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>Maps a <see cref="WerkrTask"/> and <see cref="Schedule"/> to a proto definition.</summary>
    private static ScheduledTaskDefinition MapTaskDefinition( WerkrTask task, Schedule schedule ) {
        ScheduledTaskDefinition def = new( ) {
            TaskId = task.Id,
            Name = task.Name,
            ActionType = (int) task.ActionType,
            Content = task.Content,
            TimeoutMinutes = task.TimeoutMinutes ?? 30,
            SyncIntervalMinutes = task.SyncIntervalMinutes,
            Schedule = MapScheduleDefinition( schedule ),
            SuccessCriteria = task.SuccessCriteria ?? string.Empty,
            ActionSubType = task.ActionSubType ?? string.Empty,
            ActionParametersJson = task.ActionParameters ?? string.Empty,
        };

        if (task.Arguments is { Length: > 0 }) {
            def.Arguments.AddRange( task.Arguments );
        }

        return def;
    }

    /// <summary>Maps a <see cref="Schedule"/> composite to a proto definition.</summary>
    private static ScheduleDefinition MapScheduleDefinition( Schedule schedule ) {
        ScheduleDefinition def = new( ) {
            ScheduleId = schedule.DbSchedule.Id.ToString( ),
            StopTaskAfterMinutes = schedule.DbSchedule.StopTaskAfterMinutes,
        };

        if (schedule.StartDateTime is not null) {
            def.StartDate = schedule.StartDateTime.Date.ToString( "O" );
            def.StartTime = schedule.StartDateTime.Time.ToString( "O" );
            def.TimeZoneId = schedule.StartDateTime.TimeZone.Id;
        }

        if (schedule.Expiration is not null) {
            def.ExpirationDate = schedule.Expiration.Date.ToString( "O" );
            def.ExpirationTime = schedule.Expiration.Time.ToString( "O" );
            def.ExpirationTimeZoneId = schedule.Expiration.TimeZone.Id;
        }

        if (schedule.DailyRecurrence is not null) {
            def.Daily = new DailyRecurrenceDef { DayInterval = schedule.DailyRecurrence.DayInterval };
        }

        if (schedule.WeeklyRecurrence is not null) {
            def.Weekly = new WeeklyRecurrenceDef {
                WeekInterval = schedule.WeeklyRecurrence.WeekInterval,
                RecurrenceDays = (int)schedule.WeeklyRecurrence.DaysOfWeek,
            };
        }

        if (schedule.MonthlyRecurrence is not null) {
            MonthlyRecurrenceDef monthly = new( ) {
                MonthsOfYear = (int) schedule.MonthlyRecurrence.MonthsOfYear,
                WeekNumber = schedule.MonthlyRecurrence.WeekNumber.HasValue
                    ? (int) schedule.MonthlyRecurrence.WeekNumber.Value : 0,
                DaysOfWeek = schedule.MonthlyRecurrence.DaysOfWeek.HasValue
                    ? (int) schedule.MonthlyRecurrence.DaysOfWeek.Value : 0,
            };

            if (schedule.MonthlyRecurrence.DayNumbers is { Length: > 0 }) {
                monthly.DayNumbers.AddRange( schedule.MonthlyRecurrence.DayNumbers );
            }

            def.Monthly = monthly;
        }

        if (schedule.RepeatOptions is not null) {
            def.Repeat = new RepeatOptionsDef {
                IntervalMinutes = schedule.RepeatOptions.RepeatIntervalMinutes,
                DurationMinutes = schedule.RepeatOptions.RepeatDurationMinutes,
            };
        }

        // Holiday calendar metadata
        def.HasHolidayCalendar = schedule.HolidayCalendar is not null;
        def.HolidayCalendarMode = schedule.HolidayCalendarMode?.ToString( ) ?? string.Empty;

        // Catch-up flag
        def.CatchUpEnabled = schedule.DbSchedule.CatchUpEnabled;

        return def;
    }

    /// <summary>Maps a <see cref="Workflow"/> and <see cref="Schedule"/> to a proto definition.</summary>
    private static ScheduledWorkflowDefinition MapWorkflowDefinition( Workflow workflow, Schedule schedule ) {
        ScheduledWorkflowDefinition def = new( ) {
            WorkflowId = workflow.Id,
            Name = workflow.Name,
            Schedule = MapScheduleDefinition( schedule ),
        };

        foreach (WorkflowStep step in workflow.Steps.OrderBy( s => s.Order )) {
            ScheduledWorkflowStepDef stepDef = new( ) {
                StepId = step.Id,
                TaskId = step.TaskId,
                Order = step.Order,
                ControlStatement = (int) step.ControlStatement,
                ConditionExpression = step.ConditionExpression ?? string.Empty,
                MaxIterations = step.MaxIterations,
                AgentConnectionIdOverride = step.AgentConnectionIdOverride?.ToString( ) ?? string.Empty,
                DependencyMode = (int) step.DependencyMode,
                InputVariableName = step.InputVariableName ?? string.Empty,
                OutputVariableName = step.OutputVariableName ?? string.Empty,
            };

            // Add dependency step IDs
            foreach (WorkflowStepDependency dep in step.Dependencies) {
                stepDef.DependsOnStepIds.Add( dep.DependsOnStepId );
            }

            // Include task definition if available
            if (step.Task is not null) {
                WerkrTask stepTask = step.Task;
                ScheduledTaskDefinition stepTaskDef = new( ) {
                    TaskId = stepTask.Id,
                    Name = stepTask.Name,
                    ActionType = (int) stepTask.ActionType,
                    Content = stepTask.Content,
                    TimeoutMinutes = stepTask.TimeoutMinutes ?? 30,
                    SyncIntervalMinutes = stepTask.SyncIntervalMinutes,
                    SuccessCriteria = stepTask.SuccessCriteria ?? string.Empty,
                    ActionSubType = stepTask.ActionSubType ?? string.Empty,
                    ActionParametersJson = stepTask.ActionParameters ?? string.Empty,
                };

                if (stepTask.Arguments is { Length: > 0 }) {
                    stepTaskDef.Arguments.AddRange( stepTask.Arguments );
                }

                stepDef.Task = stepTaskDef;
            }

            def.Steps.Add( stepDef );
        }

        // Populate design-time variable definitions
        foreach (WorkflowVariable variable in workflow.Variables) {
            def.Variables.Add( new WorkflowVariableDef {
                Name = variable.Name,
                DefaultValue = variable.DefaultValue ?? string.Empty,
            } );
        }

        return def;
    }

    /// <summary>
    /// Returns holiday dates for multiple schedules in a single call.
    /// Used by agents to bulk-fetch holiday data after schedule sync.
    /// </summary>
    public override async Task<EncryptedEnvelope> GetBulkScheduleHolidayDates(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        GetBulkScheduleHolidayDatesRequest inner = PayloadEncryptor.DecryptFromEnvelope<GetBulkScheduleHolidayDatesRequest>(
            request, connection.SharedKey );

        DateOnly startDate = DateOnly.Parse( inner.StartDate );
        DateOnly endDate = DateOnly.Parse( inner.EndDate );

        GetBulkScheduleHolidayDatesResponse response = new( );

        foreach (ScheduleHolidayDateQuery query in inner.Queries) {
            Guid scheduleId = Guid.Parse( query.ScheduleId );

            ScheduleHolidayCalendar? link = await holidayCalendarService.GetScheduleCalendarAsync(
                scheduleId, context.CancellationToken );

            if (link is null) {
                continue;
            }

            IReadOnlyList<HolidayDate> dates = await holidayDateService.GetDatesForRangeAsync(
                link.HolidayCalendarId, startDate, endDate, context.CancellationToken );

            ScheduleHolidayDateResult result = new( ) {
                ScheduleId = scheduleId.ToString( ),
                CalendarId = link.HolidayCalendarId.ToString( ),
                Mode = link.Mode.ToString( ),
            };

            foreach (HolidayDate hd in dates) {
                result.Dates.Add( new HolidayDateMessage {
                    Date = hd.Date.ToString( "O" ),
                    Name = hd.Name,
                    Year = hd.Year,
                    WindowStart = hd.WindowStart?.ToString( "O" ) ?? string.Empty,
                    WindowEnd = hd.WindowEnd?.ToString( "O" ) ?? string.Empty,
                    WindowTimeZoneId = hd.WindowTimeZoneId ?? string.Empty,
                } );
            }

            response.Results.Add( result );
        }

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Persists audit log entries submitted by an agent for suppressed/required holiday occurrences.
    /// </summary>
    public override async Task<EncryptedEnvelope> SubmitAuditLog(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        SubmitAuditLogRequest inner = PayloadEncryptor.DecryptFromEnvelope<SubmitAuditLogRequest>(
            request, connection.SharedKey );

        Guid scheduleId = Guid.Parse( inner.ScheduleId );

        // Look up calendar info for the schedule
        ScheduleHolidayCalendar? link = await holidayCalendarService.GetScheduleCalendarAsync(
            scheduleId, context.CancellationToken );

        string calendarName = link?.Calendar?.Name ?? "Unknown";
        HolidayCalendarMode mode = link?.Mode ?? HolidayCalendarMode.Blocklist;

        List<ScheduleAuditLog> logs = [.. inner.Entries.Select( e => new ScheduleAuditLog {
            ScheduleId = scheduleId,
            OccurrenceUtcTime = DateTime.Parse( e.OccurrenceUtc ).ToUniversalTime( ),
            CalendarName = calendarName,
            HolidayName = e.HolidayName,
            Mode = mode,
            CreatedUtc = DateTime.UtcNow,
        } )];

        dbContext.ScheduleAuditLogs.AddRange( logs );
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        SubmitAuditLogResponse response = new( ) {
            AcceptedCount = logs.Count,
        };

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Extracts the <see cref="RegisteredConnection"/> from the gRPC call context's <c>UserState</c> dictionary, where it was placed by the <see cref="Interceptors.AgentBearerTokenInterceptor"/> during authentication.
    /// </summary>
    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
