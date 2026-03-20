using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Scheduling;
using Werkr.Data;
using Werkr.Data.Entities.Registration;
using Werkr.Data.Entities.Triggers;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Api.Services;

/// <summary>
/// gRPC service for receiving file monitor trigger events from agents.
/// On receipt, validates the trigger, creates a workflow run with trigger context
/// variables, and notifies agents via schedule invalidation.
/// </summary>
/// <param name="scopeFactory">Factory for creating DI scopes.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class TriggerEventGrpcService(
    IServiceScopeFactory scopeFactory,
    ILogger<TriggerEventGrpcService> logger
) : TriggerEventService.TriggerEventServiceBase {

    /// <summary>
    /// Handles a file monitor event reported by an agent. Creates a workflow run
    /// with trigger context variables and dispatches schedule invalidation.
    /// </summary>
    public override async Task<EncryptedEnvelope> ReportFileMonitorEvent(
        EncryptedEnvelope request,
        ServerCallContext context
    ) {
        RegisteredConnection connection = GetConnection( context );
        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        FileMonitorEventRequest inner = PayloadEncryptor.DecryptFromEnvelope<FileMonitorEventRequest>(
            request, connection.SharedKey );

        if (string.IsNullOrWhiteSpace( inner.ConnectionId )) {
            throw new RpcException( new Status( StatusCode.InvalidArgument, "Connection ID is required." ) );
        }

        if (!Guid.TryParse( inner.ConnectionId, out Guid innerConnectionId )
            || innerConnectionId != connection.Id) {
            throw new RpcException( new Status(
                StatusCode.InvalidArgument,
                "Connection ID does not match authenticated connection." ) );
        }

        using IServiceScope scope = scopeFactory.CreateScope( );
        WerkrDbContext db = scope.ServiceProvider.GetRequiredService<WerkrDbContext>( );

        // Load trigger
        FileMonitorTrigger? trigger = await db.FileMonitorTriggers
            .Include( t => t.Workflow )
            .FirstOrDefaultAsync( t => t.Id == inner.TriggerId, context.CancellationToken );

        if (trigger is null) {
            FileMonitorEventResponse notFoundResp = new( ) {
                Accepted = false,
                Error = $"Trigger {inner.TriggerId} not found.",
            };
            return PayloadEncryptor.EncryptToEnvelope( notFoundResp, connection.SharedKey, keyId );
        }

        if (!trigger.Enabled) {
            FileMonitorEventResponse disabledResp = new( ) {
                Accepted = false,
                Error = $"Trigger {inner.TriggerId} is disabled.",
            };
            return PayloadEncryptor.EncryptToEnvelope( disabledResp, connection.SharedKey, keyId );
        }

        if (!trigger.Workflow.Enabled) {
            FileMonitorEventResponse wfDisabledResp = new( ) {
                Accepted = false,
                Error = $"Workflow {trigger.WorkflowId} is disabled.",
            };
            return PayloadEncryptor.EncryptToEnvelope( wfDisabledResp, connection.SharedKey, keyId );
        }

        // Create trigger context variables
        Dictionary<string, string> triggerVariables = new( ) {
            ["trigger.file_path"] = inner.FilePath,
            ["trigger.event_type"] = inner.EventType,
            ["trigger.directory"] = inner.Directory,
            ["trigger.timestamp"] = inner.Timestamp,
        };

        // Create workflow run with trigger context
        RunNowService runNowService = scope.ServiceProvider.GetRequiredService<RunNowService>( );
        (Guid scheduleId, Guid workflowRunId) = await runNowService.CreateWorkflowRunNowAsync(
            trigger.WorkflowId,
            triggerVariables: triggerVariables,
            variableSource: VariableSource.TriggerContext,
            ct: context.CancellationToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "File monitor trigger {TriggerId} fired: event={EventType}, file='{FilePath}'. " +
                "Created workflow run {RunId} for workflow {WorkflowId}.",
                inner.TriggerId, inner.EventType, inner.FilePath,
                workflowRunId, trigger.WorkflowId );
        }

        // Dispatch schedule invalidation so agents pick up the new run
        ScheduleInvalidationDispatcher invalidationDispatcher =
            scope.ServiceProvider.GetRequiredService<ScheduleInvalidationDispatcher>( );
        await invalidationDispatcher.InvalidateAsync( scheduleId, context.CancellationToken );

        FileMonitorEventResponse response = new( ) {
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
}
