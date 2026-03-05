using System.Text.Json;
using System.Threading.Channels;
using Werkr.Agent.Communication;
using Werkr.Agent.Operators;
using Werkr.Common.Models.Actions;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Scheduling;
using Werkr.Core.Tasks;
using Werkr.Data.Calendar.Enums;
using Werkr.Data.Calendar.Models;
using Werkr.Data.Entities.Schedule;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Agent.Scheduling;

/// <summary>
/// Background service that evaluates schedules locally on the Agent.
/// <para>
/// On startup (after registration), pulls assigned schedules from the Server via
/// <see cref="ScheduleSync.ScheduleSyncClient"/>, computes next fire times via
/// <see cref="ScheduleCalculator"/>, and maintains a <see cref="SortedSet{T}"/>
/// fire queue. The timer loop sleeps until the next fire time, executes the task
/// using local operators, streams output to <see cref="AgentJobOutputWriter"/>,
/// evaluates success via <see cref="SuccessCriteriaEvaluator"/>, and reports results
/// back to the Server via <see cref="JobReporting.JobReportingClient"/>.
/// </para>
/// <para>
/// Handles invalidation pushes from the Server via <see cref="Channel{String}"/>,
/// triggering an immediate re-sync when schedules change.
/// </para>
/// </summary>
/// <param name="clientFactory">Factory for creating outbound gRPC clients to the Server.</param>
/// <param name="outputWriter">Writes job output to local disk.</param>
/// <param name="successEvaluator">Evaluates success criteria.</param>
/// <param name="pwshOperator">PowerShell operator.</param>
/// <param name="shellOperator">System shell operator.</param>
/// <param name="actionOperator">Built-in action operator.</param>
/// <param name="invalidationChannel">Channel for receiving invalidation signals.</param>
/// <param name="logger">Logger.</param>
public sealed class ScheduleEvaluatorService(
    AgentGrpcClientFactory clientFactory,
    AgentJobOutputWriter outputWriter,
    SuccessCriteriaEvaluator successEvaluator,
    PwshOperator pwshOperator,
    SystemShellOperator shellOperator,
    IActionOperator actionOperator,
    Channel<string> invalidationChannel,
    ILogger<ScheduleEvaluatorService> logger
) : BackgroundService {

    // ── Internal state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a scheduled item in the fire queue.
    /// </summary>
    internal sealed record FireQueueEntry(
        DateTime FireTimeUtc,
        ScheduledTaskDefinition? Task,
        ScheduledWorkflowDefinition? Workflow
    ) : IComparable<FireQueueEntry> {
        public int CompareTo( FireQueueEntry? other ) {
            if (other is null) {
                return 1;
            }

            int cmp = FireTimeUtc.CompareTo( other.FireTimeUtc );
            if (cmp != 0) {
                return cmp;
            }
            // Break ties by task/workflow ID
            long thisId = Task?.TaskId ?? Workflow?.WorkflowId ?? 0;
            long otherId = other.Task?.TaskId ?? other.Workflow?.WorkflowId ?? 0;
            return thisId.CompareTo( otherId );
        }
    }

    private readonly SortedSet<FireQueueEntry> _fireQueue = [];
    private readonly Lock _queueLock = new( );

    /// <summary>
    /// Tracks the last sync time for each task so per-task re-sync intervals work.
    /// Key: task or workflow ID (prefixed with "t:" or "w:" to avoid collisions).
    /// </summary>
    private readonly Dictionary<string, DateTime> _lastSyncTimes = [];

    /// <summary>
    /// Stores the current assigned task definitions from the last sync.
    /// Workflows are tracked separately in <see cref="_currentWorkflows"/>.
    /// </summary>
    private readonly List<ScheduledTaskDefinition> _currentTasks = [];
    private readonly List<ScheduledWorkflowDefinition> _currentWorkflows = [];
    private readonly Lock _definitionsLock = new( );

    /// <summary>
    /// Cached holiday dates per schedule (populated during sync from bulk RPC).
    /// Key: schedule ID, Value: (mode, holiday dates).
    /// </summary>
    private readonly Dictionary<Guid, (HolidayCalendarMode Mode, IReadOnlyList<HolidayDate> Dates)> _holidayCache = [];

    // ── Constants ────────────────────────────────────────────────────────────────

    private static readonly TimeSpan s_startupDelay = TimeSpan.FromSeconds( 5 );
    private static readonly TimeSpan s_maxBackoff = TimeSpan.FromMinutes( 5 );
    private static readonly TimeSpan s_defaultTimerResolution = TimeSpan.FromSeconds( 15 );

    // ── BackgroundService ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "ScheduleEvaluatorService starting..." );
        }

        // Wait for the agent to complete registration before syncing
        await WaitForRegistrationAsync( stoppingToken );

        outputWriter.EnsureDirectoryExists( );

        // Initial sync with exponential backoff
        await SyncWithBackoffAsync( stoppingToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Schedule evaluator initialized. {TaskCount} tasks, {WorkflowCount} workflows in queue.",
                _currentTasks.Count, _currentWorkflows.Count );
        }

        // Main evaluation loop
        await RunEvaluationLoopAsync( stoppingToken );
    }

    // ── Registration Wait ────────────────────────────────────────────────────────

    /// <summary>
    /// Waits until the agent has been registered before syncing schedules.
    /// Polls every 5 seconds with exponential backoff.
    /// </summary>
    private async Task WaitForRegistrationAsync( CancellationToken ct ) {
        TimeSpan delay = s_startupDelay;
        while (!ct.IsCancellationRequested) {
            if (await clientFactory.IsRegisteredAsync( ct )) {
                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation( "Agent is registered. Starting schedule sync." );
                }
                return;
            }

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Agent not yet registered. Retrying in {Delay}.", delay );
            }
            await Task.Delay( delay, ct );
            delay = TimeSpan.FromTicks( Math.Min( delay.Ticks * 2, s_maxBackoff.Ticks ) );
        }
    }

    // ── Sync with Backoff ────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls assigned schedules from the Server with exponential backoff on failure.
    /// </summary>
    private async Task SyncWithBackoffAsync( CancellationToken ct ) {
        TimeSpan delay = TimeSpan.FromSeconds( 2 );

        for (int attempt = 1; !ct.IsCancellationRequested; attempt++) {
            try {
                await SyncSchedulesAsync( ct );
                return;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                if (logger.IsEnabled( LogLevel.Warning )) {
                    logger.LogWarning( ex, "Schedule sync attempt {Attempt} failed. Retrying in {Delay}.", attempt, delay );
                }
                await Task.Delay( delay, ct );
                delay = TimeSpan.FromTicks( Math.Min( delay.Ticks * 2, s_maxBackoff.Ticks ) );
            }
        }
    }

    // ── Full Schedule Sync ───────────────────────────────────────────────────────

    /// <summary>
    /// Pulls assigned schedules from the Server and rebuilds the fire queue.
    /// </summary>
    internal async Task SyncSchedulesAsync( CancellationToken ct ) {
        ScheduleSync.ScheduleSyncClient client = await clientFactory.CreateScheduleSyncClientAsync( ct );
        Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );

        RegisteredConnectionInfo connection = await GetConnectionInfoAsync( ct );

        AgentScheduleRequest request = new( ) {
            ConnectionId = connection.ConnectionId,
        };
        request.Tags.AddRange( connection.Tags );

        EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
            request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );
        EncryptedEnvelope responseEnvelope = await client.GetAssignedSchedulesAsync( requestEnvelope, callOptions );
        AgentScheduleResponse response = PayloadEncryptor.DecryptFromEnvelope<AgentScheduleResponse>(
            responseEnvelope, clientFactory.GetSharedKey( ) );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Synced {TaskCount} tasks and {WorkflowCount} workflows from server.",
                response.Tasks.Count, response.Workflows.Count );
        }

        // Update cached definitions
        lock (_definitionsLock) {
            _currentTasks.Clear( );
            _currentTasks.AddRange( response.Tasks );
            _currentWorkflows.Clear( );
            _currentWorkflows.AddRange( response.Workflows );
        }

        // ── Bulk-fetch holiday dates for holiday-enabled schedules ──
        await FetchBulkHolidayDatesAsync( response, ct );

        // Rebuild fire queue
        RebuildFireQueue( );

        // Update sync times
        DateTime now = DateTime.UtcNow;
        foreach (ScheduledTaskDefinition task in response.Tasks) {
            _lastSyncTimes[$"t:{task.TaskId}"] = now;
        }
        foreach (ScheduledWorkflowDefinition wf in response.Workflows) {
            _lastSyncTimes[$"w:{wf.WorkflowId}"] = now;
        }
    }

    /// <summary>
    /// Fetches holiday dates in bulk for all holiday-enabled schedules from the server.
    /// </summary>
    private async Task FetchBulkHolidayDatesAsync( AgentScheduleResponse response, CancellationToken ct ) {
        _holidayCache.Clear( );

        GetBulkScheduleHolidayDatesRequest bulkRequest = new( ) {
            StartDate = DateOnly.FromDateTime( DateTime.UtcNow ).ToString( "O" ),
            EndDate = DateOnly.FromDateTime( DateTime.UtcNow.AddHours( 24 ) ).ToString( "O" ),
        };

        // Collect holiday-enabled schedule IDs from tasks and workflows
        foreach (ScheduledTaskDefinition task in response.Tasks) {
            if (task.Schedule is not null && task.Schedule.HasHolidayCalendar) {
                bulkRequest.Queries.Add( new ScheduleHolidayDateQuery { ScheduleId = task.Schedule.ScheduleId } );
            }
        }
        foreach (ScheduledWorkflowDefinition wf in response.Workflows) {
            if (wf.Schedule is not null && wf.Schedule.HasHolidayCalendar) {
                bulkRequest.Queries.Add( new ScheduleHolidayDateQuery { ScheduleId = wf.Schedule.ScheduleId } );
            }
        }

        if (bulkRequest.Queries.Count == 0) {
            return;
        }

        try {
            ScheduleSync.ScheduleSyncClient client = await clientFactory.CreateScheduleSyncClientAsync( ct );
            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );

            EncryptedEnvelope reqEnvelope = PayloadEncryptor.EncryptToEnvelope(
                bulkRequest, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );
            EncryptedEnvelope resEnvelope = await client.GetBulkScheduleHolidayDatesAsync( reqEnvelope, callOptions );
            GetBulkScheduleHolidayDatesResponse bulkResponse = PayloadEncryptor.DecryptFromEnvelope<GetBulkScheduleHolidayDatesResponse>(
                resEnvelope, clientFactory.GetSharedKey( ) );

            foreach (ScheduleHolidayDateResult result in bulkResponse.Results) {
                if (!Guid.TryParse( result.ScheduleId, out Guid scheduleId )) {
                    continue;
                }

                if (!Enum.TryParse( result.Mode, out HolidayCalendarMode mode )) {
                    continue;
                }

                List<HolidayDate> dates = [.. result.Dates.Select( d => new HolidayDate {
                    Date = DateOnly.Parse( d.Date ),
                    Name = d.Name,
                    Year = d.Year,
                    WindowStart = !string.IsNullOrEmpty( d.WindowStart ) ? TimeOnly.Parse( d.WindowStart ) : null,
                    WindowEnd = !string.IsNullOrEmpty( d.WindowEnd ) ? TimeOnly.Parse( d.WindowEnd ) : null,
                    WindowTimeZoneId = !string.IsNullOrEmpty( d.WindowTimeZoneId ) ? d.WindowTimeZoneId : null,
                } )];

                _holidayCache[scheduleId] = (mode, dates);
            }

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Fetched holiday data for {Count} schedules.", _holidayCache.Count );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to fetch bulk holiday dates. Schedules will run without holiday filtering." );
        }
    }

    // ── Fire Queue Management ────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the fire queue from current definitions.
    /// Calculates next occurrence for each task/workflow using <see cref="ScheduleCalculator"/>.
    /// </summary>
    internal void RebuildFireQueue( ) {
        lock (_queueLock) {
            _fireQueue.Clear( );

            DateTime now = DateTime.UtcNow;
            DateTime endOfWindow = now.AddHours( 24 ); // Look ahead 24 hours

            lock (_definitionsLock) {
                foreach (ScheduledTaskDefinition task in _currentTasks) {
                    if (task.Schedule is null) {
                        continue;
                    }

                    try {
                        Schedule schedule = MapProtoToSchedule( task.Schedule );
                        DateTime? next = CalculateNextWithHolidays( schedule, now, endOfWindow );
                        if (next.HasValue && next.Value != default) {
                            _ = _fireQueue.Add( new FireQueueEntry( next.Value, task, null ) );
                        }
                    } catch (Exception ex) {
                        logger.LogWarning( ex, "Failed to calculate occurrences for task {TaskId} '{TaskName}'.",
                            task.TaskId, task.Name );
                    }
                }

                foreach (ScheduledWorkflowDefinition workflow in _currentWorkflows) {
                    if (workflow.Schedule is null) {
                        continue;
                    }

                    try {
                        Schedule schedule = MapProtoToSchedule( workflow.Schedule );
                        DateTime? next = CalculateNextWithHolidays( schedule, now, endOfWindow );
                        if (next.HasValue && next.Value != default) {
                            _ = _fireQueue.Add( new FireQueueEntry( next.Value, null, workflow ) );
                        }
                    } catch (Exception ex) {
                        logger.LogWarning( ex, "Failed to calculate occurrences for workflow {WorkflowId} '{WorkflowName}'.",
                            workflow.WorkflowId, workflow.Name );
                    }
                }
            }

            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Fire queue rebuilt: {Count} entries. Next fire: {NextFire}.",
                    _fireQueue.Count,
                    _fireQueue.Count > 0 ? _fireQueue.Min!.FireTimeUtc.ToString( "o" ) : "none" );
            }
        }
    }

    // ── Evaluation Loop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Main timer loop. Waits until the next fire time, executes the task or
    /// delegates the workflow, then advances the queue. Also listens for
    /// invalidation signals and per-task re-sync intervals.
    /// </summary>
    private async Task RunEvaluationLoopAsync( CancellationToken ct ) {
        while (!ct.IsCancellationRequested) {
            try {
                // Check for invalidation signals (non-blocking drain)
                await DrainInvalidationChannelAsync( ct );

                // Check per-task re-sync intervals
                await CheckPerTaskReSyncAsync( ct );

                // Determine sleep duration
                TimeSpan sleepDuration;
                lock (_queueLock) {
                    if (_fireQueue.Count > 0) {
                        TimeSpan untilNext = _fireQueue.Min!.FireTimeUtc - DateTime.UtcNow;
                        sleepDuration = untilNext > TimeSpan.Zero
                            ? (untilNext < s_defaultTimerResolution ? untilNext : s_defaultTimerResolution)
                            : TimeSpan.Zero;
                    } else {
                        sleepDuration = s_defaultTimerResolution;
                    }
                }

                if (sleepDuration > TimeSpan.Zero) {
                    // Use a combined wait: sleep or invalidation signal, whichever comes first
                    using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource( ct );
                    Task sleepTask = Task.Delay( sleepDuration, linked.Token );
                    Task invalidationTask = WaitForInvalidationAsync( linked.Token );

                    _ = await Task.WhenAny( sleepTask, invalidationTask );
                    await linked.CancelAsync( );
                }

                // Fire any due entries
                await FireDueEntriesAsync( ct );
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                logger.LogError( ex, "Unexpected error in schedule evaluation loop. Continuing..." );
                await Task.Delay( TimeSpan.FromSeconds( 5 ), ct );
            }
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "ScheduleEvaluatorService stopped." );
        }
    }

    /// <summary>
    /// Waits for a single invalidation signal on the channel.
    /// </summary>
    private async Task WaitForInvalidationAsync( CancellationToken ct ) {
        try {
            _ = await invalidationChannel.Reader.ReadAsync( ct );
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Invalidation signal received. Will re-sync." );
            }
            await SyncWithBackoffAsync( ct );
            RebuildFireQueue( );
        } catch (OperationCanceledException) {
            // Expected — the linked token was cancelled
        }
    }

    /// <summary>
    /// Drains all pending invalidation signals without blocking.
    /// </summary>
    private async Task DrainInvalidationChannelAsync( CancellationToken ct ) {
        bool hadInvalidations = false;
        while (invalidationChannel.Reader.TryRead( out string? scheduleId )) {
            hadInvalidations = true;
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Drained invalidation for ScheduleId={ScheduleId}.", scheduleId );
            }
        }

        if (hadInvalidations) {
            await SyncWithBackoffAsync( ct );
            RebuildFireQueue( );
        }
    }

    /// <summary>
    /// Checks if any tasks are due for periodic re-sync based on their <c>SyncIntervalMinutes</c>.
    /// </summary>
    private async Task CheckPerTaskReSyncAsync( CancellationToken ct ) {
        DateTime now = DateTime.UtcNow;
        bool needsReSync = false;

        lock (_definitionsLock) {
            foreach (ScheduledTaskDefinition task in _currentTasks) {
                string key = $"t:{task.TaskId}";
                if (_lastSyncTimes.TryGetValue( key, out DateTime lastSync )) {
                    if (now - lastSync > TimeSpan.FromMinutes( task.SyncIntervalMinutes )) {
                        needsReSync = true;
                        break;
                    }
                }
            }
        }

        if (needsReSync) {
            if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug( "Periodic re-sync interval reached. Re-syncing schedules." );
            }
            await SyncWithBackoffAsync( ct );
            RebuildFireQueue( );
        }
    }

    /// <summary>
    /// Fires all entries in the queue whose fire time is at or before now.
    /// Tasks are executed locally; workflows are delegated to the Server.
    /// </summary>
    private async Task FireDueEntriesAsync( CancellationToken ct ) {
        List<FireQueueEntry> dueEntries = [];

        lock (_queueLock) {
            DateTime now = DateTime.UtcNow;
            while (_fireQueue.Count > 0 && _fireQueue.Min!.FireTimeUtc <= now) {
                dueEntries.Add( _fireQueue.Min );
                _ = _fireQueue.Remove( _fireQueue.Min );
            }
        }

        foreach (FireQueueEntry entry in dueEntries) {
            if (ct.IsCancellationRequested) {
                break;
            }

            try {
                if (entry.Task is not null) {
                    await ExecuteTaskLocallyAsync( entry.Task, ct );
                } else if (entry.Workflow is not null) {
                    await DelegateWorkflowAsync( entry.Workflow, ct );
                }
            } catch (Exception ex) {
                string itemName = entry.Task?.Name ?? entry.Workflow?.Name ?? "unknown";
                logger.LogError( ex, "Failed to execute scheduled item '{ItemName}'.", itemName );
            }

            // Re-enqueue with next occurrence
            ReEnqueueAfterExecution( entry );
        }
    }

    /// <summary>
    /// After executing a fired entry, calculates the next occurrence and adds it back to the queue.
    /// </summary>
    private void ReEnqueueAfterExecution( FireQueueEntry entry ) {
        ScheduleDefinition? scheduleDef = entry.Task?.Schedule ?? entry.Workflow?.Schedule;
        if (scheduleDef is null) {
            return;
        }

        try {
            Schedule schedule = MapProtoToSchedule( scheduleDef );
            DateTime now = DateTime.UtcNow;
            DateTime endOfWindow = now.AddHours( 24 );
            DateTime? next = CalculateNextWithHolidays( schedule, now, endOfWindow );

            if (next.HasValue && next.Value != default) {
                lock (_queueLock) {
                    _ = _fireQueue.Add( new FireQueueEntry( next.Value, entry.Task, entry.Workflow ) );
                }
            }
        } catch (Exception ex) {
            string itemName = entry.Task?.Name ?? entry.Workflow?.Name ?? "unknown";
            logger.LogWarning( ex, "Failed to re-enqueue '{ItemName}' after execution.", itemName );
        }
    }

    /// <summary>
    /// Calculates the next occurrence using holiday-aware overload when data is cached.
    /// Also enqueues audit log submission for any suppressed occurrences.
    /// </summary>
    private DateTime? CalculateNextWithHolidays( Schedule schedule, DateTime now, DateTime endOfWindow ) {
        Guid scheduleId = schedule.DbSchedule.Id;

        if (_holidayCache.TryGetValue( scheduleId, out (HolidayCalendarMode Mode, IReadOnlyList<HolidayDate> Dates) cached )) {
            ScheduleOccurrenceResult result = ScheduleCalculator.CalculateOccurrences(
                schedule, endOfWindow, cached.Dates, cached.Mode );

            // Enqueue audit log for suppressed occurrences (fire-and-forget)
            if (result.Suppressed.Count > 0) {
                _ = Task.Run( ( ) => SubmitAuditLogAsync( scheduleId, result.Suppressed, CancellationToken.None ) );
            }

            return result.Occurrences.FirstOrDefault( o => o > now ) is var next && next != default
                ? next : null;
        }

        // No holiday data — fall back to basic calculation
        IReadOnlyList<DateTime> occurrences = ScheduleCalculator.CalculateOccurrences( schedule, endOfWindow );
        return occurrences.FirstOrDefault( o => o > now ) is var n && n != default ? n : null;
    }

    /// <summary>
    /// Submits suppressed occurrence audit log entries to the server.
    /// </summary>
    private async Task SubmitAuditLogAsync(
        Guid scheduleId,
        IReadOnlyList<SuppressedOccurrence> suppressed,
        CancellationToken ct
    ) {
        try {
            ScheduleSync.ScheduleSyncClient client = await clientFactory.CreateScheduleSyncClientAsync( ct );
            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );

            SubmitAuditLogRequest request = new( ) {
                ScheduleId = scheduleId.ToString( ),
            };

            foreach (SuppressedOccurrence s in suppressed) {
                request.Entries.Add( new AuditLogEntry {
                    OccurrenceUtc = s.UtcTime.ToString( "O" ),
                    HolidayName = s.HolidayName,
                    Reason = s.Reason,
                } );
            }

            EncryptedEnvelope reqEnvelope = PayloadEncryptor.EncryptToEnvelope(
                request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );
            _ = await client.SubmitAuditLogAsync( reqEnvelope, callOptions );
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to submit audit log for schedule {ScheduleId}.", scheduleId );
        }
    }

    // ── Local Task Execution ─────────────────────────────────────────────────────

    /// <summary>
    /// Executes a scheduled task locally using the appropriate operator.
    /// Streams output to disk, evaluates success criteria, and reports the result
    /// back to the Server.
    /// </summary>
    internal async Task ExecuteTaskLocallyAsync( ScheduledTaskDefinition taskDef, CancellationToken ct ) {
        Guid jobId = Guid.NewGuid( );
        DateTime startTime = DateTime.UtcNow;

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Executing task {TaskId} '{TaskName}' as job {JobId}.",
                taskDef.TaskId, taskDef.Name, jobId );
        }

        TaskActionType actionType = (TaskActionType) taskDef.ActionType;
        List<OperatorOutput> collectedOutput = [];
        int exitCode = 0;
        Exception? executionException = null;
        ErrorCategory errorCategory = ErrorCategory.None;

        try {
            // Apply timeout if configured
            using CancellationTokenSource timeoutCts = taskDef.TimeoutMinutes > 0
                ? CancellationTokenSource.CreateLinkedTokenSource( ct )
                : CancellationTokenSource.CreateLinkedTokenSource( ct );

            if (taskDef.TimeoutMinutes > 0) {
                timeoutCts.CancelAfter( TimeSpan.FromMinutes( taskDef.TimeoutMinutes ) );
            }

            OperatorExecution execution = RunOperator( taskDef, actionType, timeoutCts.Token );

            // Stream output to disk
            await foreach (OperatorOutput output in execution.Output.WithCancellation( timeoutCts.Token )) {
                await outputWriter.WriteLineAsync( jobId, output, timeoutCts.Token );
                collectedOutput.Add( output );
            }

            // Await the typed result
            IOperatorResult result = await execution.Result;
            exitCode = result switch {
                ShellOperatorResult shell => shell.ExitCode,
                PwshOperatorResult pwsh => pwsh.LastExitCode ?? (pwsh.HadErrors ? 1 : 0),
                _ => result.Success ? 0 : 1,
            };
            executionException = result.Exception;

            if (!result.Success && errorCategory == ErrorCategory.None) {
                errorCategory = ErrorCategory.ScriptError;
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            throw; // Propagate shutdown
        } catch (OperationCanceledException) {
            // Timeout
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
        }

        DateTime endTime = DateTime.UtcNow;

        // Evaluate success criteria
        bool success = successEvaluator.Evaluate(
            actionType,
            string.IsNullOrWhiteSpace( taskDef.SuccessCriteria ) ? null : taskDef.SuccessCriteria,
            exitCode,
            collectedOutput,
            executionException );

        // Build tail preview for the server (matches ad-hoc job behavior)
        string? tailPreview = await outputWriter.GetTailPreviewAsync( jobId, ct );

        // Report result to server
        await ReportJobResultAsync( jobId, taskDef, startTime, endTime, success, exitCode, errorCategory, null, tailPreview, ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Job {JobId} for task '{TaskName}' completed: success={Success}, exitCode={ExitCode}, duration={Duration}.",
                jobId, taskDef.Name, success, exitCode, (endTime - startTime).ToString( ) );
        }
    }

    /// <summary>
    /// Selects and invokes the appropriate operator based on the task's <see cref="TaskActionType"/>.
    /// </summary>
    private OperatorExecution RunOperator( ScheduledTaskDefinition taskDef, TaskActionType actionType, CancellationToken ct ) {
        if (actionType == TaskActionType.Action) {
            using JsonDocument parsedParameters = JsonDocument.Parse( taskDef.ActionParametersJson );
            ActionDescriptor descriptor = new( ) {
                Action = taskDef.ActionSubType,
                Parameters = parsedParameters.RootElement.Clone( ),
            };

            return actionOperator.Execute( descriptor, ct );
        }

        IShellOperator operator_ = actionType switch {
            TaskActionType.PowerShellCommand or TaskActionType.PowerShellScript => pwshOperator,
            TaskActionType.ShellCommand or TaskActionType.ShellScript => shellOperator,
            _ => throw new NotSupportedException( $"ActionType '{actionType}' is not supported for local execution." ),
        };

        return actionType switch {
            TaskActionType.PowerShellCommand or TaskActionType.ShellCommand =>
                operator_.RunCommand( taskDef.Content, ct ),

            TaskActionType.PowerShellScript or TaskActionType.ShellScript when taskDef.Arguments.Count > 0 =>
                operator_.RunScriptWithArgs( taskDef.Content, taskDef.Arguments, ct ),

            TaskActionType.PowerShellScript or TaskActionType.ShellScript =>
                operator_.RunScript( taskDef.Content, ct ),

            _ => throw new NotSupportedException( $"ActionType '{actionType}' is not supported for local execution." ),
        };
    }

    // ── Workflow Delegation ──────────────────────────────────────────────────────

    /// <summary>
    /// Delegates workflow execution to the Server. Only the Server can orchestrate
    /// multi-agent workflows because it has visibility across all agents and connectivity.
    /// </summary>
    private async Task DelegateWorkflowAsync( ScheduledWorkflowDefinition workflow, CancellationToken ct ) {
        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Delegating workflow {WorkflowId} '{WorkflowName}' to server.",
                workflow.WorkflowId, workflow.Name );
        }

        try {
            WorkflowExecution.WorkflowExecutionClient client =
                await clientFactory.CreateWorkflowExecutionClientAsync( ct );
            Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );

            RegisteredConnectionInfo connection = await GetConnectionInfoAsync( ct );

            WorkflowRunGrpcRequest innerRequest = new( ) {
                WorkflowId = workflow.WorkflowId,
                ConnectionId = connection.ConnectionId,
            };
            EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
                innerRequest, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );
            EncryptedEnvelope responseEnvelope = await client.RequestWorkflowRunAsync(
                requestEnvelope, callOptions );
            WorkflowRunGrpcResponse response = PayloadEncryptor.DecryptFromEnvelope<WorkflowRunGrpcResponse>(
                responseEnvelope, clientFactory.GetSharedKey( ) );

            if (response.Accepted) {
                if (logger.IsEnabled( LogLevel.Information )) {
                    logger.LogInformation( "Workflow {WorkflowId} accepted. RunId={RunId}.",
                        workflow.WorkflowId, response.WorkflowRunId );
                }
            } else {
                logger.LogWarning( "Workflow {WorkflowId} rejected by server: {Message}.",
                    workflow.WorkflowId, response.Message );
            }
        } catch (Exception ex) {
            logger.LogError( ex, "Failed to delegate workflow {WorkflowId} '{WorkflowName}' to server.",
                workflow.WorkflowId, workflow.Name );
        }
    }

    // ── Job Reporting ────────────────────────────────────────────────────────────

    /// <summary>Maximum number of retry attempts for reporting a job result.</summary>
    private const int ReportMaxRetries = 3;

    /// <summary>Initial delay between retry attempts.</summary>
    private static readonly TimeSpan s_reportRetryBaseDelay = TimeSpan.FromSeconds( 2 );

    /// <summary>
    /// Reports a completed job result to the Server via gRPC with retry on transient failures.
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
        CancellationToken ct
    ) {

        JobReporting.JobReportingClient client = await clientFactory.CreateJobReportingClientAsync( ct );
        RegisteredConnectionInfo connection = await GetConnectionInfoAsync( ct );

        JobResultRequest innerRequest = new( ) {
            ConnectionId = connection.ConnectionId,
            TaskId = taskDef.TaskId,
            TaskSnapshot = taskDef.Content,
            RuntimeSeconds = ( endTime - startTime ).TotalSeconds,
            StartTime = startTime.ToString( "o" ),
            EndTime = endTime.ToString( "o" ),
            Success = success,
            ExitCode = exitCode,
            ErrorCategory = (int) errorCategory,
            OutputPath = AgentJobOutputWriter.GetRelativeOutputPath( jobId ),
        };
        if (!string.IsNullOrWhiteSpace( workflowRunId )) {
            innerRequest.WorkflowRunId = workflowRunId;
        }
        if (!string.IsNullOrWhiteSpace( outputPreview )) {
            innerRequest.OutputPreview = outputPreview;
        }

        EncryptedEnvelope requestEnvelope = PayloadEncryptor.EncryptToEnvelope(
            innerRequest, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

        TimeSpan delay = s_reportRetryBaseDelay;
        for (int attempt = 1; attempt <= ReportMaxRetries; attempt++) {
            try {
                Grpc.Core.CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
                EncryptedEnvelope responseEnvelope = await client.ReportJobResultAsync( requestEnvelope, callOptions );
                JobResultResponse response = PayloadEncryptor.DecryptFromEnvelope<JobResultResponse>(
                    responseEnvelope, clientFactory.GetSharedKey( ) );

                if (response.Accepted) {
                    if (logger.IsEnabled( LogLevel.Debug )) {
                        logger.LogDebug( "Job result reported. Server JobId={ServerJobId}.", response.JobId );
                    }
                } else {
                    logger.LogWarning( "Server rejected job result for task {TaskId}.", taskDef.TaskId );
                }
                return; // Success — exit retry loop
            } catch (Exception ex) when (attempt < ReportMaxRetries && !ct.IsCancellationRequested) {
                logger.LogWarning( ex,
                    "Failed to report job result for task {TaskId} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}.",
                    taskDef.TaskId, attempt, ReportMaxRetries, delay );
                await Task.Delay( delay, ct );
                delay *= 2; // Exponential backoff
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to report job result for task {TaskId} after {MaxRetries} attempts.",
                    taskDef.TaskId, ReportMaxRetries );
            }
        }
    }

    // ── Proto ↔ Schedule Mapping ─────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="ScheduleDefinition"/> proto to a <see cref="Schedule"/> composite model
    /// for use with <see cref="ScheduleCalculator"/>.
    /// </summary>
    internal static Schedule MapProtoToSchedule( ScheduleDefinition def ) {
        TimeZoneInfo startTz = !string.IsNullOrWhiteSpace( def.TimeZoneId )
            ? TimeZoneInfo.FindSystemTimeZoneById( def.TimeZoneId )
            : TimeZoneInfo.Utc;

        DateTime startLocal = ParseDateAndTime( def.StartDate, def.StartTime );

        StartDateTimeInfo startDt = new( ) {
            Date = DateOnly.FromDateTime( startLocal ),
            Time = TimeOnly.FromDateTime( startLocal ),
            TimeZone = startTz,
        };

        // Parse schedule ID
        if (Guid.TryParse( def.ScheduleId, out Guid scheduleId )) {
            startDt.ScheduleId = scheduleId;
        }

        ExpirationDateTimeInfo? expiration = null;
        if (!string.IsNullOrWhiteSpace( def.ExpirationDate )) {
            TimeZoneInfo expTz = !string.IsNullOrWhiteSpace( def.ExpirationTimeZoneId )
                ? TimeZoneInfo.FindSystemTimeZoneById( def.ExpirationTimeZoneId )
                : startTz;
            DateTime expLocal = ParseDateAndTime( def.ExpirationDate, def.ExpirationTime );
            expiration = new( ) {
                Date = DateOnly.FromDateTime( expLocal ),
                Time = TimeOnly.FromDateTime( expLocal ),
                TimeZone = expTz,
            };
            if (Guid.TryParse( def.ScheduleId, out Guid expSchId )) {
                expiration.ScheduleId = expSchId;
            }
        }

        DailyRecurrence? daily = def.Daily is not null && def.Daily.DayInterval > 0
            ? new( ) { DayInterval = def.Daily.DayInterval }
            : null;

        WeeklyRecurrence? weekly = def.Weekly is not null && def.Weekly.WeekInterval > 0
            ? new( ) {
                WeekInterval = def.Weekly.WeekInterval,
                DaysOfWeek = (DaysOfWeek) def.Weekly.RecurrenceDays,
            }
            : null;

        MonthlyRecurrence? monthly = null;
        if (def.Monthly is not null && (def.Monthly.DayNumbers.Count > 0 || def.Monthly.DaysOfWeek > 0)) {
            monthly = new( ) {
                MonthsOfYear = (MonthsOfYear)def.Monthly.MonthsOfYear,
                WeekNumber = (WeekNumberWithinMonth)def.Monthly.WeekNumber,
                DaysOfWeek = (DaysOfWeek)def.Monthly.DaysOfWeek,
            };
            if (def.Monthly.DayNumbers.Count > 0) {
                monthly.DayNumbers = [.. def.Monthly.DayNumbers];
            }
        }

        ScheduleRepeatOptions? repeat = def.Repeat is not null && def.Repeat.IntervalMinutes > 0
            ? new( ) {
                RepeatIntervalMinutes = def.Repeat.IntervalMinutes,
                RepeatDurationMinutes = def.Repeat.DurationMinutes,
            }
            : null;

        Schedule schedule = new( ) {
            DbSchedule = new DbSchedule {
                Id = Guid.TryParse( def.ScheduleId, out Guid sid ) ? sid : Guid.Empty,
                StopTaskAfterMinutes = def.StopTaskAfterMinutes,
            },
            StartDateTime = startDt,
            Expiration = expiration,
            DailyRecurrence = daily,
            WeeklyRecurrence = weekly,
            MonthlyRecurrence = monthly,
            RepeatOptions = repeat,
        };

        // Holiday calendar metadata (lightweight — actual dates are in _holidayCache)
        if (def.HasHolidayCalendar) {
            schedule.HolidayCalendar = new HolidayCalendar { Name = "(via proto)" };
            if (Enum.TryParse( def.HolidayCalendarMode, out HolidayCalendarMode parsedMode )) {
                schedule.HolidayCalendarMode = parsedMode;
            }
        }

        return schedule;
    }

    /// <summary>
    /// Parses date and time strings from proto definitions into a <see cref="DateTime"/>.
    /// </summary>
    private static DateTime ParseDateAndTime( string date, string time ) {
        DateOnly parsedDate = DateOnly.Parse( date );
        TimeOnly parsedTime = !string.IsNullOrWhiteSpace( time )
            ? TimeOnly.Parse( time )
            : TimeOnly.MinValue;
        return parsedDate.ToDateTime( parsedTime );
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Helper record for connection info needed by gRPC calls.
    /// </summary>
    private sealed record RegisteredConnectionInfo( string ConnectionId, IReadOnlyList<string> Tags );

    /// <summary>
    /// Gets the agent's connection info from the cached connection.
    /// </summary>
    private async Task<RegisteredConnectionInfo> GetConnectionInfoAsync( CancellationToken ct ) {
        Data.Entities.Registration.RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );
        return new RegisteredConnectionInfo(
            connection.Id.ToString( ),
            connection.Tags ?? [] );
    }
}
