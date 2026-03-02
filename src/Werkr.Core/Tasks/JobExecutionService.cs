using System.Diagnostics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Orchestrates the execution of a <see cref="WerkrTask"/> as a <see cref="WerkrJob"/>.
/// Creates the job record, dispatches to the resolved agent, captures output,
/// evaluates success criteria, and finalizes the job record.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="commandDispatcher">Command dispatcher for sending commands to agents.</param>
/// <param name="agentResolver">Resolves agents by tag matching.</param>
/// <param name="outputWriter">Writes job output to disk.</param>
/// <param name="criteriaEvaluator">Evaluates success criteria against results.</param>
/// <param name="logger">Logger instance.</param>
public sealed class JobExecutionService(
    WerkrDbContext dbContext,
    ICommandDispatcher commandDispatcher,
    AgentResolver agentResolver,
    JobOutputWriter outputWriter,
    SuccessCriteriaEvaluator criteriaEvaluator,
    ILogger<JobExecutionService> logger ) {

    /// <summary>
    /// Executes a task by ID: resolves an agent, creates a job record,
    /// dispatches the command/script, captures output, and evaluates success.
    /// </summary>
    /// <param name="taskId">The task identifier to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The finalized <see cref="WerkrJob"/> record.</returns>
    /// <exception cref="KeyNotFoundException">Task not found.</exception>
    /// <exception cref="InvalidOperationException">No matching agent available.</exception>
    public async Task<WerkrJob> ExecuteAsync( long taskId, CancellationToken ct = default ) {
        // Load the task
        WerkrTask task = await dbContext.Tasks.AsNoTracking( ).FirstOrDefaultAsync( t => t.Id == taskId, ct )
            ?? throw new KeyNotFoundException( $"Task with Id={taskId} was not found." );

        return await ExecuteAsync( task, ct );
    }

    /// <summary>
    /// Executes a task: resolves an agent, creates a job record,
    /// dispatches the command/script, captures output, and evaluates success.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The finalized <see cref="WerkrJob"/> record.</returns>
    /// <exception cref="InvalidOperationException">No matching agent available.</exception>
    public async Task<WerkrJob> ExecuteAsync( WerkrTask task, CancellationToken ct = default ) {
        // Resolve agent
        RegisteredConnection agent = await agentResolver.ResolveAsync( task.TargetTags, ct )
            ?? throw new InvalidOperationException(
                $"No connected agent found matching tags [{string.Join( ", ", task.TargetTags )}]." );

        return await ExecuteOnAgentAsync( task, agent, workflowRunId: null, ct );
    }

    /// <summary>
    /// Executes a task on a specific pre-resolved agent. Creates a job record,
    /// dispatches the command/script, captures output, and evaluates success.
    /// Used by <see cref="ExecuteAsync(WerkrTask, CancellationToken)"/> after agent resolution,
    /// and by <c>WorkflowExecutor</c> which resolves agents per-step.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="agent">The pre-resolved agent connection to execute on.</param>
    /// <param name="workflowRunId">Optional workflow run ID to associate the job with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The finalized <see cref="WerkrJob"/> record.</returns>
    internal async Task<WerkrJob> ExecuteOnAgentAsync(
        WerkrTask task,
        RegisteredConnection agent,
        Guid? workflowRunId,
        CancellationToken ct = default ) {
        // Create the job record immediately (in-flight visibility)
        WerkrJob job = new( ) {
            TaskId = task.Id,
            TaskSnapshot = task.Content,
            StartTime = DateTime.UtcNow,
            AgentConnectionId = agent.Id,
            WorkflowRunId = workflowRunId,
            OutputPath = $"{Guid.Empty}.log", // placeholder until Id is generated
        };
        _ = dbContext.Jobs.Add( job );
        _ = await dbContext.SaveChangesAsync( ct );

        // Update output path with actual job Id
        job.OutputPath = $"{job.Id}.log";

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Executing task {TaskId} '{TaskName}' as job {JobId} on agent {AgentId}.",
                task.Id.ToString( ), task.Name, job.Id.ToString( ), agent.Id.ToString( ) );
        }

        // Set up timeout
        int timeoutMinutes = (int) ( task.TimeoutMinutes ?? 30 );
        using CancellationTokenSource timeoutCts = new( TimeSpan.FromMinutes( timeoutMinutes ) );
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource( ct, timeoutCts.Token );

        Stopwatch stopwatch = Stopwatch.StartNew( );
        List<OperatorOutput> collectedOutput = [];
        int? exitCode = null;
        Exception? executionException = null;
        ErrorCategory errorCategory = ErrorCategory.None;

        try {
            // Map TaskActionType to OperatorType and dispatch
            IAsyncEnumerable<OperatorOutput> outputStream = DispatchTask( task, agent.Id, linkedCts.Token );

            // Consume output stream, writing each line to disk incrementally
            await foreach (OperatorOutput output in outputStream.WithCancellation( linkedCts.Token )) {
                collectedOutput.Add( output );
                await outputWriter.WriteLineAsync( job.Id, output, CancellationToken.None );
            }

            // Try to extract exit code from output (convention: last line with "ExitCode: N")
            exitCode = ExtractExitCode( collectedOutput );
        } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
            errorCategory = ErrorCategory.Timeout;
            executionException = new TimeoutException(
                $"Job {job.Id} timed out after {timeoutMinutes} minutes." );
            logger.LogWarning( "Job {JobId} timed out after {Timeout} minutes.", job.Id.ToString( ), timeoutMinutes.ToString( ) );
        } catch (CommandDispatcherException cde) {
            errorCategory = MapDispatchFailure( cde.Reason );
            executionException = cde;
            logger.LogError( cde, "Job {JobId} dispatch failed: {Reason}.", job.Id.ToString( ), cde.Reason.ToString( ) );
        } catch (OperationCanceledException ex) when (ct.IsCancellationRequested) {
            errorCategory = ErrorCategory.Unknown;
            executionException = ex;
            logger.LogWarning( "Job {JobId} was cancelled.", job.Id.ToString( ) );
        } catch (Exception ex) {
            errorCategory = ErrorCategory.ScriptError;
            executionException = ex;
            logger.LogError( ex, "Job {JobId} failed with unexpected error.", job.Id.ToString( ) );
        }

        stopwatch.Stop( );

        // Evaluate success
        bool success = criteriaEvaluator.Evaluate(
            task.ActionType, task.SuccessCriteria, exitCode, collectedOutput, executionException );

        // Get tail preview
        string? tailPreview = await outputWriter.GetTailPreviewAsync( job.Id, CancellationToken.None );

        // Finalize the job record
        job.EndTime = DateTime.UtcNow;
        job.RuntimeSeconds = stopwatch.Elapsed.TotalSeconds;
        job.Success = success;
        job.ExitCode = exitCode;
        job.ErrorCategory = errorCategory;
        job.Output = tailPreview;

        _ = await dbContext.SaveChangesAsync( CancellationToken.None );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Job {JobId} completed: Success={Success}, ExitCode={ExitCode}, Runtime={Runtime:F1}s, ErrorCategory={ErrorCategory}.",
                job.Id.ToString( ), success.ToString( ), exitCode?.ToString( ) ?? "null",
                stopwatch.Elapsed.TotalSeconds, errorCategory.ToString( ) );
        }

        return job;
    }

    /// <summary>
    /// Retrieves job history for a task, ordered by most recent first.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="limit">Maximum number of jobs to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of jobs for the specified task.</returns>
    public async Task<IReadOnlyList<WerkrJob>> GetJobHistoryAsync( long taskId, int limit = 50, CancellationToken ct = default ) =>
        await dbContext.Jobs.AsNoTracking( )
            .Include( j => j.Task )
            .Include( j => j.AgentConnection )
            .Where( j => j.TaskId == taskId )
            .OrderByDescending( j => j.StartTime )
            .Take( limit )
            .ToListAsync( ct );

    /// <summary>
    /// Retrieves recent jobs across all tasks, ordered by most recent first.
    /// Supports optional filtering by success status and date range.
    /// </summary>
    /// <param name="success">Optional filter — <c>true</c> for successful, <c>false</c> for failed, <c>null</c> for all.</param>
    /// <param name="since">Optional start of date/time window (UTC).</param>
    /// <param name="until">Optional end of date/time window (UTC).</param>
    /// <param name="limit">Maximum number of jobs to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of recent jobs.</returns>
    public async Task<IReadOnlyList<WerkrJob>> GetRecentJobsAsync(
        bool? success = null,
        DateTime? since = null,
        DateTime? until = null,
        int limit = 50,
        CancellationToken ct = default ) {
        IQueryable<WerkrJob> query = dbContext.Jobs.AsNoTracking( )
            .Include( j => j.Task )
            .Include( j => j.AgentConnection );

        if (success.HasValue) {
            query = query.Where( j => j.Success == success.Value );
        }

        if (since.HasValue) {
            query = query.Where( j => j.StartTime >= since.Value );
        }

        if (until.HasValue) {
            query = query.Where( j => j.StartTime <= until.Value );
        }

        return await query
            .OrderByDescending( j => j.StartTime )
            .Take( limit )
            .ToListAsync( ct );
    }

    /// <summary>
    /// Retrieves a single job by ID.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The job, or null if not found.</returns>
    public async Task<WerkrJob?> GetJobAsync( Guid jobId, CancellationToken ct = default ) =>
        await dbContext.Jobs.AsNoTracking( ).FirstOrDefaultAsync( j => j.Id == jobId, ct );

    /// <summary>
    /// Retrieves the full output for a job from disk.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full output text, or null if the file does not exist.</returns>
    public async Task<string?> GetJobOutputAsync( Guid jobId, CancellationToken ct = default ) =>
        await outputWriter.ReadFullOutputAsync( jobId, ct );

    /// <summary>
    /// Maps a <see cref="TaskActionType"/> to an <see cref="OperatorType"/>
    /// and dispatches the appropriate command or script.
    /// </summary>
    private IAsyncEnumerable<OperatorOutput> DispatchTask(
        WerkrTask task, Guid agentConnectionId, CancellationToken ct ) {

        return task.ActionType switch {
            TaskActionType.PowerShellCommand =>
                commandDispatcher.ExecuteCommandAsync( agentConnectionId, OperatorType.PowerShell, task.Content, ct ),

            TaskActionType.PowerShellScript =>
                task.Arguments is { Length: > 0 }
                    ? commandDispatcher.ExecuteScriptAsync( agentConnectionId, OperatorType.PowerShell, task.Content, task.Arguments, ct )
                    : commandDispatcher.ExecuteScriptAsync( agentConnectionId, OperatorType.PowerShell, task.Content, null, ct ),

            TaskActionType.ShellCommand =>
                commandDispatcher.ExecuteCommandAsync( agentConnectionId, OperatorType.SystemShell, task.Content, ct ),

            TaskActionType.ShellScript =>
                task.Arguments is { Length: > 0 }
                    ? commandDispatcher.ExecuteScriptAsync( agentConnectionId, OperatorType.SystemShell, task.Content, task.Arguments, ct )
                    : commandDispatcher.ExecuteScriptAsync( agentConnectionId, OperatorType.SystemShell, task.Content, null, ct ),

            TaskActionType.Action =>
                commandDispatcher.ExecuteActionAsync( agentConnectionId,
                    CreateActionDescriptor( task ), ct ),

            _ => throw new InvalidOperationException( $"Unsupported action type: {task.ActionType}." )
        };
    }

    private static ActionDescriptor CreateActionDescriptor( WerkrTask task ) {
        using JsonDocument parsedParameters = JsonDocument.Parse( task.ActionParameters ?? "{}" );
        return new ActionDescriptor {
            Action = task.ActionSubType ?? throw new InvalidOperationException( "ActionSubType is required for Action tasks." ),
            Parameters = parsedParameters.RootElement.Clone( ),
        };
    }

    /// <summary>
    /// Maps <see cref="CommandDispatchFailure"/> to <see cref="ErrorCategory"/>.
    /// </summary>
    private static ErrorCategory MapDispatchFailure( CommandDispatchFailure failure ) =>
        failure switch {
            CommandDispatchFailure.AgentNotFound => ErrorCategory.AgentUnreachable,
            CommandDispatchFailure.AgentRevoked => ErrorCategory.AgentUnreachable,
            CommandDispatchFailure.AgentUnreachable => ErrorCategory.AgentUnreachable,
            CommandDispatchFailure.TlsError => ErrorCategory.AgentUnreachable,
            CommandDispatchFailure.EncryptionError => ErrorCategory.Unknown,
            _ => ErrorCategory.Unknown
        };

    /// <summary>
    /// Attempts to extract an exit code from the output stream.
    /// Looks for the last output line that matches the convention used by
    /// the operators: an Information-level line ending with <c>exited with code N</c>.
    /// </summary>
    private static int? ExtractExitCode( List<OperatorOutput> output ) {
        // Look for exit code in reverse order (most likely in last few lines)
        for (int i = output.Count - 1; i >= Math.Max( 0, output.Count - 10 ); i--) {
            string message = output[i].Message;

            // Pattern: "Process exited with code 0" or "exited with code 123"
            int idx = message.LastIndexOf( "exited with code ", StringComparison.OrdinalIgnoreCase );
            if (idx >= 0) {
                string codeStr = message[( idx + "exited with code ".Length )..].Trim( );
                if (int.TryParse( codeStr, out int code )) {
                    return code;
                }
            }
        }

        return null;
    }
}
