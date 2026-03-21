using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Common.Protos;
using Werkr.Core.Audit;
using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Triggers;
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
/// <param name="auditService">Audit service for recording schedule audit events.</param>
/// <param name="credentialService">Credential service for resolving credentials at dispatch time.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class ScheduleSyncGrpcService(
    WerkrDbContext dbContext,
    ScheduleService scheduleService,
    HolidayDateService holidayDateService,
    HolidayCalendarService holidayCalendarService,
    IAuditService auditService,
    Werkr.Core.Credentials.ICredentialService credentialService,
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
            await ResolveCredentialsForTaskDefAsync( taskDef, connection.Id, context.CancellationToken );
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
            // If workflow has TargetTags, use those for agent matching; otherwise fall back to per-task tags
            bool anyMatch = workflow.TargetTags is { Length: > 0 }
                ? workflow.TargetTags.Any( tag => agentTags.Contains( tag.Trim( ) ) )
                : workflow.Steps.Any( step =>
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

            // Load child workflows for composite steps
            List<long> compositeStepIds = [.. workflow.Steps
                .Where( s => s.IsComposite )
                .Select( s => s.Id )];

            if (compositeStepIds.Count > 0) {
                List<Workflow> childWorkflows = await dbContext.Workflows
                    .AsNoTracking( )
                    .Include( w => w.Steps )
                        .ThenInclude( s => s.Task )
                    .Include( w => w.Steps )
                        .ThenInclude( s => s.Dependencies )
                    .Include( w => w.Variables )
                    .Where( w => w.IsChildWorkflow && w.ParentStepId != null
                        && compositeStepIds.Contains( w.ParentStepId.Value ) )
                    .ToListAsync( context.CancellationToken );

                foreach (Workflow childWf in childWorkflows) {
                    ChildWorkflowDefinition childDef = new( ) {
                        ChildWorkflowId = childWf.Id,
                    };

                    foreach (WorkflowStep childStep in childWf.Steps.OrderBy( s => s.Order )) {
                        ScheduledWorkflowStepDef childStepDef = new( ) {
                            StepId = childStep.Id,
                            TaskId = childStep.TaskId ?? 0,
                            Order = childStep.Order,
                            ControlStatement = (int) childStep.ControlStatement,
                            ConditionExpression = childStep.ConditionExpression ?? string.Empty,
                            MaxIterations = childStep.MaxIterations,
                            AgentConnectionIdOverride = childStep.AgentConnectionIdOverride?.ToString( ) ?? string.Empty,
                            DependencyMode = (int) childStep.DependencyMode,
                            InputVariableName = childStep.InputVariableName ?? string.Empty,
                            OutputVariableName = childStep.OutputVariableName ?? string.Empty,
                            IsComposite = childStep.IsComposite,
                            CompositeType = (int) childStep.CompositeType,
                            ChildWorkflowId = childStep.ChildWorkflowId ?? 0,
                            IterationVariableName = childStep.IterationVariableName ?? string.Empty,
                            CollectionVariableName = childStep.CollectionVariableName ?? string.Empty,
                        };

                        foreach (WorkflowStepDependency dep in childStep.Dependencies) {
                            childStepDef.DependsOnStepIds.Add( dep.DependsOnStepId );
                        }

                        if (childStep.Task is not null) {
                            WerkrTask childTask = childStep.Task;
                            ScheduledTaskDefinition childTaskDef = new( ) {
                                TaskId = childTask.Id,
                                Name = childTask.Name,
                                ActionType = (int) childTask.ActionType,
                                Content = childTask.Content,
                                TimeoutMinutes = childTask.TimeoutMinutes ?? 60,
                                SyncIntervalMinutes = childTask.SyncIntervalMinutes,
                                SuccessCriteria = childTask.SuccessCriteria ?? string.Empty,
                                ActionSubType = childTask.ActionSubType ?? string.Empty,
                                ActionParametersJson = childTask.ActionParameters ?? string.Empty,
                            };

                            if (childTask.Arguments is { Length: > 0 }) {
                                childTaskDef.Arguments.AddRange( childTask.Arguments );
                            }

                            childStepDef.Task = childTaskDef;
                        }

                        childDef.Steps.Add( childStepDef );
                    }

                    foreach (WorkflowVariable variable in childWf.Variables) {
                        childDef.Variables.Add( new WorkflowVariableDef {
                            Name = variable.Name,
                            DefaultValue = variable.DefaultValue ?? string.Empty,
                            DataType = variable.DataType ?? string.Empty,
                            IsRequired = variable.IsRequired,
                            LogRedaction = variable.LogRedaction,
                        } );
                    }

                    workflowDef.ChildWorkflows.Add( childDef );
                }
            }

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

                // Include step IDs that already succeeded (for retry-from-failed pre-population)
                List<long> succeededStepIds = await dbContext.WorkflowStepExecutions
                    .AsNoTracking()
                    .Where(e => e.WorkflowRunId == ws.WorkflowRunId.Value
                        && e.Status == StepExecutionStatus.Succeeded)
                    .Select(e => e.StepId)
                    .Distinct()
                    .ToListAsync(context.CancellationToken);

                workflowDef.PriorSucceededStepIds.AddRange( succeededStepIds );

                // Include latest variable values for retry cache seeding
                if (succeededStepIds.Count > 0) {
                    List<WorkflowRunVariable> latestVars = await dbContext.Set<WorkflowRunVariable>()
                        .AsNoTracking()
                        .Where(v => v.WorkflowRunId == ws.WorkflowRunId.Value)
                        .GroupBy(v => v.VariableName)
                        .Select(g => g.OrderByDescending(v => v.Version).First())
                        .ToListAsync(context.CancellationToken);

                    foreach (WorkflowRunVariable rv in latestVars) {
                        workflowDef.RunVariableValues[rv.VariableName] = rv.Value;
                    }
                }
            }

            // Resolve credentials for all task definitions in the workflow
            await ResolveCredentialsForWorkflowDefAsync( workflowDef, connection.Id, context.CancellationToken );

            response.Workflows.Add( workflowDef );
        }

        // ── File monitor triggers ──
        List<FileMonitorTrigger> fileMonitorTriggers = await dbContext.FileMonitorTriggers
            .AsNoTracking( )
            .Where( t => t.Enabled )
            .ToListAsync( context.CancellationToken );

        foreach (FileMonitorTrigger fmt in fileMonitorTriggers) {
            // Tag matching: if TargetTags is null/empty, the trigger matches all agents
            if (!string.IsNullOrWhiteSpace( fmt.TargetTags )) {
                try {
                    string[]? triggerTags = System.Text.Json.JsonSerializer
                        .Deserialize<string[]>( fmt.TargetTags );
                    if (triggerTags is { Length: > 0 }
                        && !triggerTags.Any( tag => agentTags.Contains( tag.Trim( ) ) )) {
                        continue;
                    }
                } catch {
                    // Malformed JSON — skip tag filter
                }
            }

            // Parse event types from JSON string
            List<string> eventTypes = [];
            try {
                string[]? parsed = System.Text.Json.JsonSerializer
                    .Deserialize<string[]>( fmt.EventTypes );
                if (parsed is not null) {
                    eventTypes.AddRange( parsed );
                }
            } catch {
                eventTypes.Add( "created" );
            }

            FileMonitorTriggerDef triggerDef = new( ) {
                TriggerId = fmt.Id,
                WorkflowId = fmt.WorkflowId,
                WatchDirectory = fmt.WatchDirectory,
                FilePattern = fmt.FilePattern,
                DebounceMs = fmt.DebounceMs,
            };
            triggerDef.EventTypes.AddRange( eventTypes );

            response.FileMonitorTriggers.Add( triggerDef );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Returning {TaskCount} tasks, {WorkflowCount} workflows, and {TriggerCount} file monitor triggers for agent {AgentId}.",
                response.Tasks.Count.ToString( ), response.Workflows.Count.ToString( ),
                response.FileMonitorTriggers.Count.ToString( ), inner.ConnectionId );
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
            TimeoutMinutes = task.TimeoutMinutes ?? 60,
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

    /// <summary>
    /// Resolves credentials for all task definitions within a workflow definition,
    /// including child workflow steps.
    /// </summary>
    private async Task ResolveCredentialsForWorkflowDefAsync(
        ScheduledWorkflowDefinition workflowDef, Guid agentConnectionId, CancellationToken ct
    ) {
        foreach (ScheduledWorkflowStepDef step in workflowDef.Steps) {
            if (step.Task is not null) {
                await ResolveCredentialsForTaskDefAsync( step.Task, agentConnectionId, ct );
            }
        }

        foreach (ChildWorkflowDefinition child in workflowDef.ChildWorkflows) {
            foreach (ScheduledWorkflowStepDef childStep in child.Steps) {
                if (childStep.Task is not null) {
                    await ResolveCredentialsForTaskDefAsync( childStep.Task, agentConnectionId, ct );
                }
            }
        }
    }

    /// <summary>
    /// Resolves credentials referenced in a task definition's ActionParameters
    /// and populates the proto's resolved_credentials map.
    /// </summary>
    private async Task ResolveCredentialsForTaskDefAsync(
        ScheduledTaskDefinition taskDef, Guid agentConnectionId, CancellationToken ct
    ) {
        IReadOnlyList<string> credentialNames = Werkr.Core.Credentials.CredentialResolver
            .FindCredentialReferences( taskDef.ActionParametersJson );

        foreach (string name in credentialNames) {
            try {
                Common.Models.CredentialResolveResult result = await credentialService.ResolveForAgentAsync(
                    name, agentConnectionId, "system", ct );

                if (result is { Found: true, InScope: true, DecryptedValue: not null }) {
                    taskDef.ResolvedCredentials[name] = result.DecryptedValue;
                } else if (result is { Found: true, InScope: false }) {
                    logger.LogError(
                        "Credential '{CredentialName}' exists but agent {AgentId} is out of scope (task {TaskId}). Dispatch rejected.",
                        name, agentConnectionId, taskDef.TaskId );
                    throw new Grpc.Core.RpcException( new Grpc.Core.Status(
                        Grpc.Core.StatusCode.PermissionDenied,
                        $"Credential '{name}' is not scoped to this agent." ) );
                } else if (!result.Found) {
                    logger.LogWarning(
                        "Credential '{CredentialName}' not found for task {TaskId}.",
                        name, taskDef.TaskId );
                }
            } catch (Grpc.Core.RpcException) {
                throw; // Re-throw scope rejections
            } catch (Exception ex) {
                logger.LogWarning( ex,
                    "Failed to resolve credential '{CredentialName}' for task {TaskId}.",
                    name, taskDef.TaskId );
            }
        }
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

        // ShiftMode (Epic 1.4.4)
        def.ShiftMode = (int)schedule.DbSchedule.ShiftMode;

        // Calendar definition with rules (Epic 1.4.3)
        if (schedule.HolidayCalendar is not null) {
            CalendarDefinition calDef = new( ) {
                CalendarId = schedule.HolidayCalendar.Id.ToString( ),
                Name = schedule.HolidayCalendar.Name,
                WorkingDays = (int) schedule.HolidayCalendar.WorkingDays,
            };

            foreach (HolidayRule rule in schedule.HolidayCalendar.Rules) {
                calDef.HolidayRules.Add( new HolidayRuleDefinition {
                    Name = rule.Name,
                    RuleType = (int)rule.RuleType,
                    Month = rule.Month ?? 0,
                    Day = rule.Day ?? 0,
                    DayOfWeek = rule.DayOfWeek.HasValue ? (int)rule.DayOfWeek.Value : 0,
                    WeekNumber = rule.WeekNumber ?? 0,
                    ObservanceRule = (int)rule.ObservanceRule,
                    YearStart = rule.YearStart ?? 0,
                    YearEnd = rule.YearEnd ?? 0,
                    WindowStart = rule.WindowStart?.ToString( "O" ) ?? string.Empty,
                    WindowEnd = rule.WindowEnd?.ToString( "O" ) ?? string.Empty,
                    WindowTimeZoneId = rule.WindowTimeZoneId ?? string.Empty,
                } );
            }

            def.Calendar = calDef;
        }

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
                TaskId = step.TaskId ?? 0,
                Order = step.Order,
                ControlStatement = (int) step.ControlStatement,
                ConditionExpression = step.ConditionExpression ?? string.Empty,
                MaxIterations = step.MaxIterations,
                AgentConnectionIdOverride = step.AgentConnectionIdOverride?.ToString( ) ?? string.Empty,
                DependencyMode = (int) step.DependencyMode,
                InputVariableName = step.InputVariableName ?? string.Empty,
                OutputVariableName = step.OutputVariableName ?? string.Empty,
                IsComposite = step.IsComposite,
                CompositeType = (int) step.CompositeType,
                ChildWorkflowId = step.ChildWorkflowId ?? 0,
                IterationVariableName = step.IterationVariableName ?? string.Empty,
                CollectionVariableName = step.CollectionVariableName ?? string.Empty,
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
                    TimeoutMinutes = stepTask.TimeoutMinutes ?? 60,
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
                DataType = variable.DataType ?? string.Empty,
                IsRequired = variable.IsRequired,
                LogRedaction = variable.LogRedaction,
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
    /// Persists audit log entries submitted by an agent for suppressed/shifted holiday occurrences.
    /// Writes to the unified <see cref="Werkr.Data.Entities.Audit.AuditEvent"/> table.
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

        int accepted = 0;
        foreach (AuditLogEntry e in inner.Entries) {
            string action = !string.IsNullOrEmpty( e.Action ) ? e.Action : "Suppressed";
            string eventTypeId = string.Equals( action, "Shifted", StringComparison.OrdinalIgnoreCase )
                ? AuditEventType.ScheduleOccurrenceShifted.ToEventId( )
                : AuditEventType.ScheduleOccurrenceSuppressed.ToEventId( );

            object details = new {
                ScheduleId = scheduleId.ToString( ),
                OccurrenceUtcTime = e.OccurrenceUtc,
                CalendarName = calendarName,
                HolidayName = e.HolidayName,
                ShiftedToUtcTime = !string.IsNullOrEmpty( e.ShiftedToUtc ) ? e.ShiftedToUtc : null
            };

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: eventTypeId,
                ActorId: connection.Id.ToString( ),
                ActorType: "Agent",
                EntityType: "Schedule",
                EntityId: scheduleId.ToString( ),
                ActionPerformed: action,
                Details: details
            ), context.CancellationToken );
            accepted++;
        }

        SubmitAuditLogResponse response = new( ) {
            AcceptedCount = accepted,
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
