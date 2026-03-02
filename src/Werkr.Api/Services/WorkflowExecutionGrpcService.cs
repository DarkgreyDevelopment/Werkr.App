using Grpc.Core;

using Microsoft.EntityFrameworkCore;

using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Workflows;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for handling Agent-initiated workflow execution requests.
/// When an Agent's schedule triggers a workflow, the Agent delegates execution
/// back to the Server because only the Server can orchestrate multi-agent workflows.
/// All RPCs use <see cref="EncryptedEnvelope"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="workflowExecutor">Workflow executor for DAG orchestration.</param>
/// <param name="logger">Logger instance.</param>
public sealed class WorkflowExecutionGrpcService(
    WerkrDbContext dbContext,
    WorkflowExecutor workflowExecutor,
    ILogger<WorkflowExecutionGrpcService> logger
) : WorkflowExecution.WorkflowExecutionBase {

    /// <summary>
    /// Handles an Agent's request to run a workflow. Loads the workflow
    /// and delegates to <see cref="WorkflowExecutor.ExecuteAsync"/>.
    /// </summary>
    public override async Task<EncryptedEnvelope> RequestWorkflowRun(
        EncryptedEnvelope request,
        ServerCallContext context ) {

        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        WorkflowRunGrpcRequest inner = PayloadEncryptor.DecryptFromEnvelope<WorkflowRunGrpcRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Agent {AgentId} requesting workflow {WorkflowId} execution.",
                inner.ConnectionId, inner.WorkflowId.ToString( ) );
        }

        Workflow? workflow = await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Task )
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .FirstOrDefaultAsync( w => w.Id == inner.WorkflowId, context.CancellationToken );

        if (workflow is null) {
            WorkflowRunGrpcResponse notFoundResponse = new( ) {
                Accepted = false,
                Message = $"Workflow with Id={inner.WorkflowId} not found.",
            };
            return PayloadEncryptor.EncryptToEnvelope( notFoundResponse, connection.SharedKey, keyId );
        }

        if (!workflow.Enabled) {
            WorkflowRunGrpcResponse disabledResponse = new( ) {
                Accepted = false,
                Message = $"Workflow '{workflow.Name}' is disabled.",
            };
            return PayloadEncryptor.EncryptToEnvelope( disabledResponse, connection.SharedKey, keyId );
        }

        try {
            WorkflowRun run = await workflowExecutor.ExecuteAsync( workflow, context.CancellationToken );

            if (logger.IsEnabled( LogLevel.Information )) {
                logger.LogInformation(
                    "Workflow run {RunId} initiated for workflow {WorkflowId} '{WorkflowName}' by agent {AgentId}.",
                    run.Id.ToString( ), workflow.Id.ToString( ), workflow.Name, inner.ConnectionId );
            }

            WorkflowRunGrpcResponse response = new( ) {
                Accepted = true,
                WorkflowRunId = run.Id.ToString( ),
                Message = $"Workflow run {run.Id} initiated.",
            };
            return PayloadEncryptor.EncryptToEnvelope( response, connection.SharedKey, keyId );
        } catch (Exception ex) {
            logger.LogError( ex, "Failed to execute workflow {WorkflowId} requested by agent {AgentId}.",
                inner.WorkflowId.ToString( ), inner.ConnectionId );

            WorkflowRunGrpcResponse errorResponse = new( ) {
                Accepted = false,
                Message = $"Failed to execute workflow: {ex.Message}",
            };
            return PayloadEncryptor.EncryptToEnvelope( errorResponse, connection.SharedKey, keyId );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
