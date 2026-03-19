using System.Globalization;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for receiving job results reported by Agents.
/// Agents call <see cref="ReportJobResult"/> after completing a scheduled task execution.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="jobBroadcaster">Singleton broadcaster for SSE push notifications.</param>
/// <param name="workflowBroadcaster">Singleton broadcaster for workflow SSE events.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class JobReportingGrpcService(
    WerkrDbContext dbContext,
    JobEventBroadcaster jobBroadcaster,
    WorkflowEventBroadcaster workflowBroadcaster,
    ILogger<JobReportingGrpcService> logger
) : JobReporting.JobReportingBase {

    /// <summary>
    /// Receives a job result from an agent and persists it as a <see cref="WerkrJob"/> entity.
    /// </summary>
    public override async Task<EncryptedEnvelope> ReportJobResult(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        JobResultRequest inner = PayloadEncryptor.DecryptFromEnvelope<JobResultRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        if (!Guid.TryParse( inner.ConnectionId, out Guid connectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid connection_id format." ) );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Receiving job result from agent {AgentId} for task {TaskId}.",
                inner.ConnectionId, inner.TaskId.ToString( ) );
        }

        // Map error category
        ErrorCategory errorCategory = Enum.IsDefined( typeof( ErrorCategory ), inner.ErrorCategory )
            ? (ErrorCategory) inner.ErrorCategory
            : ErrorCategory.Unknown;

        // Parse timestamps
        DateTime startTime = DateTime.TryParse(inner.StartTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime st)
            ? st
            : DateTime.UtcNow;
        DateTime? endTime = DateTime.TryParse(inner.EndTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime et)
            ? et
            : null;

        // Parse workflow run ID if provided
        Guid? workflowRunId = !string.IsNullOrWhiteSpace( inner.WorkflowRunId )
            && Guid.TryParse( inner.WorkflowRunId, out Guid wfRunId )
            ? wfRunId
            : null;

        // Parse agent-assigned job ID for upsert (if provided)
        Guid? agentJobId = !string.IsNullOrWhiteSpace( inner.JobId )
            && Guid.TryParse( inner.JobId, out Guid parsedJobId )
            ? parsedJobId
            : null;

        // Parse schedule ID (if provided)
        Guid? scheduleId = !string.IsNullOrWhiteSpace( inner.ScheduleId )
            && Guid.TryParse( inner.ScheduleId, out Guid parsedScheduleId )
            ? parsedScheduleId
            : null;

        // Parse step ID (if provided, for workflow step linkage)
        long? stepId = inner.StepId > 0 ? inner.StepId : null;

        // Upsert: if agent provided a job_id, check for existing job
        WerkrJob? job = agentJobId.HasValue
            ? await dbContext.Jobs.FirstOrDefaultAsync( j => j.Id == agentJobId.Value, context.CancellationToken )
            : null;

        if (job is not null) {
            // Update existing job (agent persisted it first, now server catches up)
            job.TaskId = inner.TaskId;
            job.TaskSnapshot = inner.TaskSnapshot;
            job.RuntimeSeconds = inner.RuntimeSeconds;
            job.StartTime = startTime;
            job.EndTime = endTime;
            job.Success = inner.Success;
            job.AgentConnectionId = connectionId;
            job.ExitCode = inner.ExitCode;
            job.ErrorCategory = errorCategory;
            job.Output = string.IsNullOrWhiteSpace( inner.OutputPreview ) ? null : inner.OutputPreview;
            job.OutputPath = inner.OutputPath;
            job.WorkflowRunId = workflowRunId;
            job.ScheduleId = scheduleId;
            job.StepId = stepId;
        } else {
            // Create new job
            job = new( ) {
                TaskId = inner.TaskId,
                TaskSnapshot = inner.TaskSnapshot,
                RuntimeSeconds = inner.RuntimeSeconds,
                StartTime = startTime,
                EndTime = endTime,
                Success = inner.Success,
                AgentConnectionId = connectionId,
                ExitCode = inner.ExitCode,
                ErrorCategory = errorCategory,
                Output = string.IsNullOrWhiteSpace( inner.OutputPreview ) ? null : inner.OutputPreview,
                OutputPath = inner.OutputPath,
                WorkflowRunId = workflowRunId,
                ScheduleId = scheduleId,
                StepId = stepId,
            };
            if (agentJobId.HasValue) {
                job.Id = agentJobId.Value;
            }
            _ = dbContext.Jobs.Add( job );
        }
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        // Broadcast to SSE subscribers after the job is safely persisted.
        jobBroadcaster.Publish( new JobEvent(
            JobId: job.Id,
            TaskId: job.TaskId,
            WorkflowRunId: job.WorkflowRunId,
            Success: job.Success,
            ExitCode: job.ExitCode,
            RuntimeSeconds: job.RuntimeSeconds,
            AgentConnectionId: connectionId,
            Timestamp: DateTime.UtcNow
        ) );

        // Publish workflow step event and update step execution if this is a workflow job
        if (workflowRunId.HasValue && stepId.HasValue) {
            // Update WorkflowStepExecution to Completed/Failed
            WorkflowStepExecution? stepExecution = await dbContext.WorkflowStepExecutions
                .Include( se => se.Step )
                    .ThenInclude( s => s!.Task )
                .Where( se => se.WorkflowRunId == workflowRunId.Value && se.StepId == stepId.Value )
                .OrderByDescending( se => se.Attempt )
                .FirstOrDefaultAsync( context.CancellationToken );

            if (stepExecution is not null) {
                stepExecution.Status = inner.Success ? StepExecutionStatus.Succeeded : StepExecutionStatus.Failed;
                stepExecution.EndTime = endTime ?? DateTime.UtcNow;
                stepExecution.JobId = job.Id;
                if (!inner.Success) {
                    stepExecution.ErrorMessage = inner.OutputPreview?[..Math.Min( inner.OutputPreview.Length, 4000 )];
                }
                _ = await dbContext.SaveChangesAsync( context.CancellationToken );
            } else {
                logger.LogWarning(
                    "No WorkflowStepExecution found for run {RunId}, step {StepId}. ReportStepStarted may not have been called.",
                    workflowRunId.Value, stepId.Value );
            }

            // Resolve step name for event
            string stepName = stepExecution?.Step?.Task?.Name ?? $"Step {stepId.Value}";

            if (inner.Success) {
                workflowBroadcaster.Publish( new StepCompletedEvent(
                    WorkflowRunId: workflowRunId.Value,
                    StepId: stepId.Value,
                    StepName: stepName,
                    JobId: job.Id,
                    ExitCode: inner.ExitCode,
                    RuntimeSeconds: inner.RuntimeSeconds,
                    Timestamp: DateTime.UtcNow,
                    Attempt: stepExecution?.Attempt ?? 1,
                    StartTime: stepExecution?.StartTime,
                    EndTime: stepExecution?.EndTime
                ) );
            } else {
                workflowBroadcaster.Publish( new StepFailedEvent(
                    WorkflowRunId: workflowRunId.Value,
                    StepId: stepId.Value,
                    StepName: stepName,
                    JobId: job.Id,
                    ExitCode: inner.ExitCode,
                    ErrorMessage: inner.OutputPreview,
                    Timestamp: DateTime.UtcNow,
                    Attempt: stepExecution?.Attempt ?? 1,
                    StartTime: stepExecution?.StartTime,
                    EndTime: stepExecution?.EndTime
                ) );
            }
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Persisted job {JobId} from agent {AgentId}: Task={TaskId}, Success={Success}, Runtime={Runtime:F1}s.",
                job.Id.ToString( ), inner.ConnectionId, inner.TaskId.ToString( ),
                inner.Success.ToString( ), inner.RuntimeSeconds );
        }

        JobResultResponse response = new( ) {
            Accepted = true,
            JobId = job.Id.ToString( ),
        };
        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Extracts the authenticated <see cref="RegisteredConnection"/> from the gRPC call context's user state.
    /// </summary>
    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }

    /// <summary>
    /// Receives a step-started notification from an agent and creates a <see cref="WorkflowStepExecution"/> record.
    /// </summary>
    public override async Task<EncryptedEnvelope> ReportStepStarted(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        StepStartedRequest inner = PayloadEncryptor.DecryptFromEnvelope<StepStartedRequest>(
            request, connection.SharedKey );

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        DateTime startTime = DateTime.TryParse(inner.StartTime, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime st)
            ? st
            : DateTime.UtcNow;

        // Determine attempt number (previous max + 1 for retries)
        int maxAttempt = await dbContext.WorkflowStepExecutions
            .Where( se => se.WorkflowRunId == runId && se.StepId == inner.StepId )
            .MaxAsync( se => (int?) se.Attempt, context.CancellationToken ) ?? 0;

        int attempt = maxAttempt + 1;

        WorkflowStepExecution execution = new( ) {
            WorkflowRunId = runId,
            StepId = inner.StepId,
            Attempt = attempt,
            Status = StepExecutionStatus.Running,
            StartTime = startTime,
        };
        _ = dbContext.WorkflowStepExecutions.Add( execution );
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        workflowBroadcaster.Publish( new StepStartedEvent(
            WorkflowRunId: runId,
            StepId: inner.StepId,
            StepName: inner.StepName,
            TaskId: inner.TaskId,
            Timestamp: DateTime.UtcNow,
            Attempt: attempt,
            StartTime: startTime
        ) );

        StepEventResponse response = new( ) { Accepted = true };
        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Receives a step-skipped notification from an agent and creates a <see cref="WorkflowStepExecution"/> record.
    /// </summary>
    public override async Task<EncryptedEnvelope> ReportStepSkipped(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        StepSkippedRequest inner = PayloadEncryptor.DecryptFromEnvelope<StepSkippedRequest>(
            request, connection.SharedKey );

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        int maxAttempt = await dbContext.WorkflowStepExecutions
            .Where( se => se.WorkflowRunId == runId && se.StepId == inner.StepId )
            .MaxAsync( se => (int?) se.Attempt, context.CancellationToken ) ?? 0;

        WorkflowStepExecution execution = new( ) {
            WorkflowRunId = runId,
            StepId = inner.StepId,
            Attempt = maxAttempt + 1,
            Status = StepExecutionStatus.Skipped,
            SkipReason = inner.Reason,
        };
        _ = dbContext.WorkflowStepExecutions.Add( execution );
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        workflowBroadcaster.Publish( new StepSkippedEvent(
            WorkflowRunId: runId,
            StepId: inner.StepId,
            StepName: inner.StepName,
            Reason: inner.Reason,
            Timestamp: DateTime.UtcNow,
            Attempt: maxAttempt + 1
        ) );

        StepEventResponse response = new( ) { Accepted = true };
        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }
}
