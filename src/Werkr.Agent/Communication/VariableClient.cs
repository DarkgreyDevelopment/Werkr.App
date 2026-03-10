using Microsoft.Extensions.Options;
using Werkr.Core.Communication;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Communication;

/// <summary>
/// Wraps the <see cref="VariableService.VariableServiceClient"/> gRPC client,
/// handling encrypted envelope serialization and connection resolution.
/// Provides typed methods for variable get/set and workflow run creation.
/// </summary>
/// <param name="clientFactory">Factory for creating outbound gRPC clients.</param>
/// <param name="variableOptions">Workflow variable configuration for size limits.</param>
/// <param name="logger">Logger instance.</param>
public sealed class VariableClient(
    AgentGrpcClientFactory clientFactory,
    IOptions<WorkflowVariableOptions> variableOptions,
    ILogger<VariableClient> logger
) {

    /// <summary>
    /// Fetches the latest version of a variable from the API.
    /// Returns <see langword="null"/> if the variable was not found or the call failed.
    /// </summary>
    /// <param name="workflowRunId">The workflow run ID.</param>
    /// <param name="variableName">The variable name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="GetVariableResponse"/>, or <see langword="null"/> on failure.</returns>
    public async Task<GetVariableResponse?> GetVariableAsync(
        Guid workflowRunId,
        string variableName,
        CancellationToken ct
    ) {
        try {
            VariableService.VariableServiceClient client =
                await clientFactory.CreateVariableServiceClientAsync( ct );
            RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

            GetVariableRequest request = new( ) {
                ConnectionId = connection.Id.ToString( ),
                WorkflowRunId = workflowRunId.ToString( ),
                VariableName = variableName,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            EncryptedEnvelope responseEnvelope = await client.GetVariableAsync( envelope, callOptions );

            return PayloadEncryptor.DecryptFromEnvelope<GetVariableResponse>(
                responseEnvelope, clientFactory.GetSharedKey( ) );
        } catch (Exception ex) {
            logger.LogWarning( ex,
                "Failed to get variable '{Name}' for run {RunId}.",
                variableName, workflowRunId );
            return null;
        }
    }

    /// <summary>
    /// Pushes a variable value to the API. Failures are logged but do not throw —
    /// the local cache remains the source of truth for subsequent steps.
    /// </summary>
    /// <param name="workflowRunId">The workflow run ID.</param>
    /// <param name="variableName">The variable name.</param>
    /// <param name="value">The JSON variable value.</param>
    /// <param name="stepId">The producing step ID.</param>
    /// <param name="jobId">The producing job ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PushVariableAsync(
        Guid workflowRunId,
        string variableName,
        string value,
        long stepId,
        Guid jobId,
        CancellationToken ct
    ) {
        try {
            int maxBytes = variableOptions.Value.MaxValueSizeBytes;
            int valueBytes = System.Text.Encoding.UTF8.GetByteCount( value );
            if (valueBytes > maxBytes) {
                logger.LogWarning(
                    "Variable '{Name}' value size ({Bytes} bytes) exceeds MaxValueSizeBytes ({Max}). Skipping push.",
                    variableName, valueBytes, maxBytes );
                return;
            }

            VariableService.VariableServiceClient client =
                await clientFactory.CreateVariableServiceClientAsync( ct );
            RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

            SetVariableRequest request = new( ) {
                ConnectionId = connection.Id.ToString( ),
                WorkflowRunId = workflowRunId.ToString( ),
                VariableName = variableName,
                Value = value,
                ProducedByStepId = stepId,
                ProducedByJobId = jobId.ToString( ),
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            EncryptedEnvelope responseEnvelope = await client.SetVariableAsync( envelope, callOptions );
            SetVariableResponse response = PayloadEncryptor.DecryptFromEnvelope<SetVariableResponse>(
                responseEnvelope, clientFactory.GetSharedKey( ) );

            if (!response.Accepted) {
                logger.LogWarning(
                    "Server rejected variable '{Name}' for run {RunId}: {Error}",
                    variableName, workflowRunId, response.Error );
            } else if (logger.IsEnabled( LogLevel.Debug )) {
                logger.LogDebug(
                    "Pushed variable '{Name}' v{Version} for run {RunId}.",
                    variableName, response.Version, workflowRunId );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex,
                "Failed to push variable '{Name}' for run {RunId}. Local cache still valid.",
                variableName, workflowRunId );
        }
    }

    /// <summary>
    /// Creates a workflow run on the API and seeds default variable values.
    /// Used for cron-triggered workflows where no API-side run was pre-created.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The API-generated workflow run ID, or <see langword="null"/> on failure.</returns>
    public async Task<Guid?> CreateWorkflowRunAsync(
        long workflowId,
        CancellationToken ct
    ) {
        try {
            VariableService.VariableServiceClient client =
                await clientFactory.CreateVariableServiceClientAsync( ct );
            RegisteredConnection connection = await clientFactory.GetConnectionAsync( ct );

            CreateWorkflowRunRequest request = new( ) {
                ConnectionId = connection.Id.ToString( ),
                WorkflowId = workflowId,
            };

            EncryptedEnvelope envelope = PayloadEncryptor.EncryptToEnvelope(
                request, clientFactory.GetSharedKey( ), clientFactory.GetKeyId( ) );

            CallOptions callOptions = clientFactory.CreateCallOptions( cancellationToken: ct );
            EncryptedEnvelope responseEnvelope = await client.CreateWorkflowRunAsync( envelope, callOptions );
            CreateWorkflowRunResponse response = PayloadEncryptor.DecryptFromEnvelope<CreateWorkflowRunResponse>(
                responseEnvelope, clientFactory.GetSharedKey( ) );

            if (!response.Accepted) {
                logger.LogWarning(
                    "Server rejected CreateWorkflowRun for workflow {WorkflowId}: {Error}",
                    workflowId, response.Error );
                return null;
            }

            if (Guid.TryParse( response.WorkflowRunId, out Guid runId )) {
                if (logger.IsEnabled( LogLevel.Debug )) {
                    logger.LogDebug(
                        "Created WorkflowRun {RunId} on API for workflow {WorkflowId}.",
                        runId, workflowId );
                }
                return runId;
            }

            logger.LogWarning(
                "Server returned invalid workflow_run_id '{Id}' for workflow {WorkflowId}.",
                response.WorkflowRunId, workflowId );
            return null;
        } catch (Exception ex) {
            logger.LogWarning( ex,
                "Failed to create WorkflowRun for workflow {WorkflowId}. Falling back to local ID.",
                workflowId );
            return null;
        }
    }
}
