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
/// gRPC service implementation for PowerShell operations.
/// Authenticates via <c>BearerTokenInterceptor</c>, decrypts <see cref="EncryptedEnvelope"/>
/// requests, streams encrypted output back in envelopes.
/// </summary>
/// <remarks>Creates a new <see cref="PwshService"/>.</remarks>
/// <param name="pwshOperator">The PowerShell operator.</param>
/// <param name="agentSettingsOptions">Agent settings from configuration.</param>
/// <param name="logger">Logger for diagnostics.</param>
public class PwshService(
    PwshOperator pwshOperator,
    IOptions<AgentSettings> agentSettingsOptions,
    ILogger<PwshService> logger
) : Pwsh.PwshBase {
    private readonly AgentSettings _agentSettings = agentSettingsOptions.Value;

    /// <inheritdoc/>
    public override async Task RunCommand(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidatePowerShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ShellRequest shellRequest = PayloadEncryptor.DecryptFromEnvelope<ShellRequest>( request, connection.SharedKey );
        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing PowerShell command for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = pwshOperator.RunCommand( shellRequest.Command, context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    /// <inheritdoc/>
    public override async Task RunScript(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidatePowerShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ShellRequest shellRequest = PayloadEncryptor.DecryptFromEnvelope<ShellRequest>( request, connection.SharedKey );
        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing PowerShell script for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = pwshOperator.RunScript( shellRequest.Command, context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    /// <inheritdoc/>
    public override async Task RunScriptWithArgs(
        EncryptedEnvelope request,
        IServerStreamWriter<EncryptedEnvelope> responseStream,
        ServerCallContext context ) {

        ValidatePowerShellEnabled( );
        RegisteredConnection connection = GetConnection( context );

        ScriptRequest scriptRequest = PayloadEncryptor.DecryptFromEnvelope<ScriptRequest>( request, connection.SharedKey );

        if (logger.IsEnabled( LogLevel.Debug )) {
            logger.LogDebug( "Executing PowerShell script with args for connection {ConnectionId}.", connection.Id.ToString( ) );
        }

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        OperatorExecution execution = pwshOperator.RunScriptWithArgs( scriptRequest.Script, [.. scriptRequest.Args], context.CancellationToken );
        await OperatorOutputAdapter.StreamToGrpc( execution.Output, responseStream, connection.SharedKey, keyId, context.CancellationToken );
    }

    private void ValidatePowerShellEnabled( ) {
        if (!_agentSettings.EnablePowerShell) {
            throw new RpcException( new Status( StatusCode.Unimplemented, "PowerShell is disabled on this agent." ) );
        }
    }

    private static RegisteredConnection GetConnection( ServerCallContext context ) {
        return context.UserState.TryGetValue( "Connection", out object? connObj ) && connObj is RegisteredConnection connection
            ? connection
            : throw new RpcException( new Status( StatusCode.Internal, "Connection not resolved by interceptor." ) );
    }
}
