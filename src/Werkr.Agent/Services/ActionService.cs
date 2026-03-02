using System.Text.Json;

using Grpc.Core;

using Werkr.Agent.Protos;
using Werkr.Common.Models.Actions;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// gRPC service implementation for built-in action operations.
/// Authenticates via <c>BearerTokenInterceptor</c>, decrypts <see cref="EncryptedEnvelope"/>
/// requests, streams encrypted output back in envelopes.
/// </summary>
/// <remarks>Creates a new <see cref="ActionService"/>.</remarks>
/// <param name="actionOperator">The action operator for dispatching built-in actions.</param>
/// <param name="logger">Logger for diagnostics.</param>
public class ActionService(
    IActionOperator actionOperator,
    ILogger<ActionService> logger
) : Werkr.Agent.Protos.Action.ActionBase {

    /// <inheritdoc/>
    public override async Task RunAction(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        RegisteredConnection connection = GetConnection( context );

        ActionRequest actionRequest = PayloadEncryptor.DecryptFromEnvelope<ActionRequest>(
            request, connection.SharedKey );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing action '{ActionName}' for connection {ConnectionId}.",
                actionRequest.ActionName, connection.Id.ToString( ) );
        }

        using JsonDocument parsedParameters = JsonDocument.Parse( actionRequest.ParametersJson );
        ActionDescriptor descriptor = new( ) {
            Action = actionRequest.ActionName,
            Parameters = parsedParameters.RootElement.Clone( ),
        };

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = actionOperator.Execute( descriptor, context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc(
            execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
