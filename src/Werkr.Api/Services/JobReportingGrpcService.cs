using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for receiving job results reported by Agents.
/// Agents call <see cref="ReportJobResult"/> after completing a scheduled task execution.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="broadcaster">Singleton broadcaster for SSE push notifications.</param>
/// <param name="logger">Logger instance.</param>
public sealed class JobReportingGrpcService(
    WerkrDbContext dbContext,
    JobEventBroadcaster broadcaster,
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

        Guid connectionId = Guid.Parse( inner.ConnectionId );

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
        DateTime startTime = DateTime.TryParse( inner.StartTime, out DateTime st )
            ? DateTime.SpecifyKind( st, DateTimeKind.Utc )
            : DateTime.UtcNow;
        DateTime? endTime = DateTime.TryParse( inner.EndTime, out DateTime et )
            ? DateTime.SpecifyKind( et, DateTimeKind.Utc )
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
            };
            if (agentJobId.HasValue) {
                job.Id = agentJobId.Value;
            }
            _ = dbContext.Jobs.Add( job );
        }
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        // Broadcast to SSE subscribers after the job is safely persisted.
        broadcaster.Publish( new JobEvent(
            JobId: job.Id,
            TaskId: job.TaskId,
            WorkflowRunId: job.WorkflowRunId,
            Success: job.Success,
            ExitCode: job.ExitCode,
            RuntimeSeconds: job.RuntimeSeconds,
            AgentConnectionId: connectionId,
            Timestamp: DateTime.UtcNow
        ) );

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
}
