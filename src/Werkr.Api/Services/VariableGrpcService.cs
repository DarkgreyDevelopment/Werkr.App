using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Werkr.Common.Configuration;
using Werkr.Common.Models;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for agent variable get/set during workflow execution.
/// Hosted by the API. All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="variableOptions">Variable size configuration.</param>
/// <param name="workflowBroadcaster">Workflow event broadcaster for run lifecycle events.</param>
/// <param name="outputStreaming">Output streaming service for rate-limit state cleanup.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class VariableGrpcService(
    WerkrDbContext dbContext,
    IOptions<WorkflowVariableOptions> variableOptions,
    WorkflowEventBroadcaster workflowBroadcaster,
    OutputStreamingGrpcService outputStreaming,
    ILogger<VariableGrpcService> logger
) : VariableService.VariableServiceBase {

    /// <summary>
    /// Returns the latest version of a variable for a given workflow run.
    /// </summary>
    public override async Task<EncryptedEnvelope> GetVariable(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        GetVariableRequest inner = PayloadEncryptor.DecryptFromEnvelope<GetVariableRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        WorkflowRunVariable? latest = await dbContext.WorkflowRunVariables
            .AsNoTracking( )
            .Where( v => v.WorkflowRunId == runId
                && v.VariableName == inner.VariableName )
            .OrderByDescending( v => v.Version )
            .FirstOrDefaultAsync( context.CancellationToken );

        GetVariableResponse response = latest is null
            ? new GetVariableResponse { Found = false }
            : new GetVariableResponse {
                Found = true,
                Value = latest.Value,
                Version = latest.Version,
            };
        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "GetVariable for run {RunId}, variable '{Name}': found={Found}, version={Version}.",
                inner.WorkflowRunId, inner.VariableName,
                response.Found.ToString( ), response.Version.ToString( ) );
        }

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Appends a new version of a variable value for a given workflow run.
    /// Version numbering is serialized per (RunId, VariableName) using a database transaction.
    /// </summary>
    public override async Task<EncryptedEnvelope> SetVariable(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        SetVariableRequest inner = PayloadEncryptor.DecryptFromEnvelope<SetVariableRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        // Validate JSON
        if (!IsValidJson( inner.Value )) {
            SetVariableResponse badJsonResponse = new( ) {
                Accepted = false,
                Error = "Value is not valid JSON.",
            };
            return PayloadEncryptor.EncryptToEnvelope( badJsonResponse, connection.SharedKey, keyId );
        }

        // Validate size
        int maxSize = variableOptions.Value.MaxValueSizeBytes;
        if (System.Text.Encoding.UTF8.GetByteCount( inner.Value ) > maxSize) {
            SetVariableResponse tooBigResponse = new( ) {
                Accepted = false,
                Error = $"Value exceeds maximum size of {maxSize} bytes.",
            };
            return PayloadEncryptor.EncryptToEnvelope( tooBigResponse, connection.SharedKey, keyId );
        }

        // Parse optional FK values
        long? producedByStepId = inner.ProducedByStepId > 0 ? inner.ProducedByStepId : null;
        Guid? producedByJobId = !string.IsNullOrWhiteSpace( inner.ProducedByJobId )
            && Guid.TryParse( inner.ProducedByJobId, out Guid jobId )
                ? jobId : null;

        // Determine next version (serialized via transaction)
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx =
            await dbContext.Database.BeginTransactionAsync( context.CancellationToken );

        int currentMaxVersion = await dbContext.WorkflowRunVariables
            .Where( v => v.WorkflowRunId == runId && v.VariableName == inner.VariableName )
            .MaxAsync( v => (int?) v.Version, context.CancellationToken ) ?? 0;

        int nextVersion = currentMaxVersion + 1;

        WorkflowRunVariable entry = new( ) {
            WorkflowRunId = runId,
            VariableName = inner.VariableName,
            Value = inner.Value,
            Version = nextVersion,
            ProducedByStepId = producedByStepId,
            ProducedByJobId = producedByJobId,
            Source = VariableSource.StepOutput,
            Created = DateTime.UtcNow,
        };

        _ = dbContext.WorkflowRunVariables.Add( entry );
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );
        await tx.CommitAsync( context.CancellationToken );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug(
                "SetVariable for run {RunId}, variable '{Name}': version={Version}.",
                inner.WorkflowRunId, inner.VariableName, nextVersion.ToString( ) );
        }

        SetVariableResponse response = new( ) {
            Accepted = true,
            Version = nextVersion,
        };

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Creates a <see cref="WorkflowRun"/> and seeds default variable values.
    /// Used by agents for cron-triggered workflows where no API-side run was pre-created.
    /// </summary>
    public override async Task<EncryptedEnvelope> CreateWorkflowRun(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        CreateWorkflowRunRequest inner = PayloadEncryptor.DecryptFromEnvelope<CreateWorkflowRunRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        Workflow? workflow = await dbContext.Set<Workflow>( )
            .Include( w => w.Variables )
            .FirstOrDefaultAsync( w => w.Id == inner.WorkflowId, context.CancellationToken );

        if (workflow is null) {
            CreateWorkflowRunResponse notFoundResponse = new( ) {
                Accepted = false,
                Error = $"Workflow {inner.WorkflowId} not found.",
            };
            return PayloadEncryptor.EncryptToEnvelope( notFoundResponse, connection.SharedKey, keyId );
        }

        Guid workflowRunId = Guid.NewGuid( );
        WorkflowRun run = new( ) {
            Id = workflowRunId,
            WorkflowId = inner.WorkflowId,
            StartTime = DateTime.UtcNow,
            Status = WorkflowRunStatus.Running,
        };
        _ = dbContext.Set<WorkflowRun>( ).Add( run );

        // Seed default variable values
        foreach (WorkflowVariable variable in workflow.Variables) {
            if (!string.IsNullOrWhiteSpace( variable.DefaultValue )) {
                WorkflowRunVariable defaultEntry = new( ) {
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

        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created WorkflowRun {RunId} for cron-triggered workflow {WorkflowId} with {VarCount} seeded default variables.",
                workflowRunId.ToString( ), inner.WorkflowId.ToString( ),
                workflow.Variables.Count( v => !string.IsNullOrWhiteSpace( v.DefaultValue ) ).ToString( ) );
        }

        CreateWorkflowRunResponse response = new( ) {
            Accepted = true,
            WorkflowRunId = workflowRunId.ToString( ),
        };

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Extracts the <see cref="RegisteredConnection"/> from the gRPC call context's <c>UserState</c> dictionary.
    /// </summary>
    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }

    /// <summary>
    /// Sets <c>WorkflowRun.Status</c> to Completed or Failed and publishes a <see cref="RunCompletedEvent"/>.
    /// </summary>
    public override async Task<EncryptedEnvelope> CompleteWorkflowRun(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        CompleteWorkflowRunRequest inner = PayloadEncryptor.DecryptFromEnvelope<CompleteWorkflowRunRequest>(
            request, connection.SharedKey );

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        DateTime endTime = DateTime.TryParse( inner.EndTime, out DateTime et )
            ? DateTime.SpecifyKind( et, DateTimeKind.Utc )
            : DateTime.UtcNow;

        WorkflowRun? run = await dbContext.WorkflowRuns
            .FirstOrDefaultAsync( r => r.Id == runId, context.CancellationToken ) ?? throw new RpcException( new Status( StatusCode.NotFound, $"WorkflowRun {runId} not found." ) );
        run.Status = inner.Success ? WorkflowRunStatus.Completed : WorkflowRunStatus.Failed;
        run.EndTime = endTime;
        _ = await dbContext.SaveChangesAsync( context.CancellationToken );

        // Count step results for the event
        int completedSteps = await dbContext.WorkflowStepExecutions
            .CountAsync( se => se.WorkflowRunId == runId && se.Status == StepExecutionStatus.Completed,
                context.CancellationToken );
        int failedSteps = await dbContext.WorkflowStepExecutions
            .CountAsync( se => se.WorkflowRunId == runId && se.Status == StepExecutionStatus.Failed,
                context.CancellationToken );

        long? failedStepId = inner.FailedStepId > 0 ? inner.FailedStepId : null;

        workflowBroadcaster.Publish( new RunCompletedEvent(
            WorkflowRunId: runId,
            Success: inner.Success,
            FailedStepId: failedStepId,
            CompletedSteps: completedSteps,
            FailedSteps: failedSteps,
            Timestamp: DateTime.UtcNow
        ) );

        outputStreaming.CleanupRun( runId );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Workflow run {RunId} finalized: Success={Success}, Completed={Completed}, Failed={Failed}.",
                runId.ToString( ), inner.Success.ToString( ),
                completedSteps.ToString( ), failedSteps.ToString( ) );
        }

        CompleteWorkflowRunResponse response = new( ) { Accepted = true };
        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Returns step execution records for a workflow run (used by the agent for retry skip logic).
    /// </summary>
    public override async Task<EncryptedEnvelope> GetStepExecutions(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        GetStepExecutionsRequest inner = PayloadEncryptor.DecryptFromEnvelope<GetStepExecutionsRequest>(
            request, connection.SharedKey );

        if (!Guid.TryParse( inner.WorkflowRunId, out Guid runId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Invalid workflow_run_id." ) );
        }

        // Return the latest attempt per step
        List<WorkflowStepExecution> executions = await dbContext.WorkflowStepExecutions
            .Where( se => se.WorkflowRunId == runId )
            .OrderByDescending( se => se.Attempt )
            .ToListAsync( context.CancellationToken );

        GetStepExecutionsResponse response = new( );

        HashSet<long> seen = [];
        foreach (WorkflowStepExecution exec in executions) {
            if (seen.Add( exec.StepId )) {
                response.Entries.Add( new StepExecutionEntry {
                    StepId = exec.StepId,
                    Attempt = exec.Attempt,
                    Status = exec.Status.ToString( ),
                } );
            }
        }

        return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
    }

    /// <summary>
    /// Validates that a string is valid JSON.
    /// </summary>
    private static bool IsValidJson( string value ) {
        if (string.IsNullOrWhiteSpace( value )) {
            return false;
        }

        try {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse( value );
            return true;
        } catch (System.Text.Json.JsonException) {
            return false;
        }
    }
}
