using Grpc.Core;
using Microsoft.Extensions.Options;

using Werkr.Agent.Operators;
using Werkr.Agent.Protos;
using Werkr.Common.Configuration;
using Werkr.Common.Protos;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Data.Entities.Registration;

namespace Werkr.Agent.Services;

/// <summary>
/// gRPC service implementation for system shell operations (cmd.exe / bash).
/// Authenticates via <c>BearerTokenInterceptor</c>, decrypts <see cref="EncryptedEnvelope"/>
/// requests, streams encrypted output back in envelopes.
/// </summary>
/// <remarks>Creates a new <see cref="SystemShellService"/>.</remarks>
/// <param name="shellOperator">The system shell operator.</param>
/// <param name="agentSettingsOptions">Agent settings from configuration.</param>
/// <param name="logger">Logger for diagnostics.</param>
public class SystemShellService(
    SystemShellOperator shellOperator,
    IOptions<AgentSettings> agentSettingsOptions,
    ILogger<SystemShellService> logger
) : SystemShell.SystemShellBase {
    private readonly AgentSettings _agentSettings = agentSettingsOptions.Value;

    /// <inheritdoc/>
    public override async Task RunCommand(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidateSystemShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ShellRequest shellRequest = PayloadEncryptor.DecryptFromEnvelope<ShellRequest>( request, connection.SharedKey );
        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing system shell command for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = shellOperator.RunCommand( shellRequest.Command, context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    /// <inheritdoc/>
    public override async Task RunScript(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidateSystemShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ShellRequest shellRequest = PayloadEncryptor.DecryptFromEnvelope<ShellRequest>( request, connection.SharedKey );
        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing system shell script for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = shellOperator.RunScript( shellRequest.Command, context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    /// <inheritdoc/>
    public override async Task RunScriptWithArgs(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidateSystemShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ScriptRequest scriptRequest = PayloadEncryptor.DecryptFromEnvelope<ScriptRequest>( request, connection.SharedKey );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing system shell script with args for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = shellOperator.RunScriptWithArgs( scriptRequest.Script, [.. scriptRequest.Args], context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    private void ValidateSystemShellEnabled( ) {
        if (!_agentSettings.EnableSystemShell) {
            throw new RpcException( new Status( StatusCode.Unimplemented, "System shell is disabled on this agent." ) );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
