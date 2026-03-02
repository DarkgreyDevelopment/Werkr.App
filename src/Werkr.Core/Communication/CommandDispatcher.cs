using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

using Grpc.Core;

using Microsoft.Extensions.Logging;

using Werkr.Agent.Protos;
using Werkr.Common.Models;
using Werkr.Common.Models.Actions;
using Werkr.Common.Protos;
using Werkr.Data.Entities.Registration;

namespace Werkr.Core.Communication;

/// <summary>
/// Orchestrates sending commands to Agents via gRPC and yielding decrypted output.
/// Single entry point for all command execution. All payloads are wrapped in
/// <see cref="EncryptedEnvelope"/> using the connection's SharedKey.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="CommandDispatcher"/> class.</remarks>
/// <param name="connectionManager">Agent connection manager for channel resolution.</param>
/// <param name="logger">Logger instance.</param>
public sealed class CommandDispatcher(
    AgentConnectionManager connectionManager,
    ILogger<CommandDispatcher> logger
) : ICommandDispatcher {

    /// <summary>
    /// Executes a command on the specified agent and yields decrypted output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side <see cref="RegisteredConnection.Id"/>.</param>
    /// <param name="operatorType">The operator type to use (PowerShell, SystemShell).</param>
    /// <param name="command">The plaintext command to execute.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/abort.</param>
    /// <returns>An async enumerable of decrypted <see cref="OperatorOutput"/> records.</returns>
    public async IAsyncEnumerable<OperatorOutput> ExecuteCommandAsync(
        Guid agentConnectionId,
        OperatorType operatorType,
        string command,
        [EnumeratorCancellation] CancellationToken cancellationToken = default ) {

        (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection connection) =
            await ResolveChannelAsync( agentConnectionId, cancellationToken );

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        EncryptedEnvelope envelope = EncryptRequest(
            new ShellRequest { Command = command }, connection.SharedKey, keyId, agentConnectionId );

        Guid callId = Guid.NewGuid( );
        CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
            connection, callId, cancellationToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Dispatching {OperatorType} command to Agent {AgentId}, CallId {CallId}.",
                operatorType.ToString( ), agentConnectionId.ToString( ), callId.ToString( ) );
        }

        AsyncServerStreamingCall<EncryptedEnvelope> call;

        switch (operatorType) {
            case OperatorType.PowerShell:
                Pwsh.PwshClient pwshClient = new( channel );
                call = pwshClient.RunCommand( envelope, callOptions );
                break;

            case OperatorType.SystemShell:
                SystemShell.SystemShellClient shellClient = new( channel );
                call = shellClient.RunCommand( envelope, callOptions );
                break;

            default:
                yield return OperatorOutput.Create(
                    "Error", $"Unsupported operator type: {operatorType}" );
                yield break;
        }

        using (call) {
            IAsyncEnumerable<OperatorOutput> stream = GrpcOutputReader.ReadAsync(
                call.ResponseStream, connection.SharedKey, cancellationToken );

            IAsyncEnumerator<OperatorOutput> enumerator = stream.GetAsyncEnumerator( cancellationToken );
            try {
                while (true) {
                    bool moved;
                    try {
                        moved = await enumerator.MoveNextAsync( );
                    } catch (Exception ex) {
                        throw TranslateException( ex, agentConnectionId );
                    }
                    if (!moved) {
                        break;
                    }

                    yield return enumerator.Current;
                }
            } finally {
                await enumerator.DisposeAsync( );
            }
        }
    }

    /// <summary>
    /// Executes a script on the specified agent and yields decrypted output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side <see cref="RegisteredConnection.Id"/>.</param>
    /// <param name="operatorType">The operator type to use (PowerShell, SystemShell).</param>
    /// <param name="scriptPath">The script path to execute.</param>
    /// <param name="args">Optional script arguments.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/abort.</param>
    /// <returns>An async enumerable of decrypted <see cref="OperatorOutput"/> records.</returns>
    public async IAsyncEnumerable<OperatorOutput> ExecuteScriptAsync(
        Guid agentConnectionId,
        OperatorType operatorType,
        string scriptPath,
        IEnumerable<string>? args,
        [EnumeratorCancellation] CancellationToken cancellationToken = default ) {

        (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection connection) =
            await ResolveChannelAsync( agentConnectionId, cancellationToken );

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );
        Guid callId = Guid.NewGuid( );
        CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
            connection, callId, cancellationToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Dispatching {OperatorType} script to Agent {AgentId}, CallId {CallId}.",
                operatorType.ToString( ), agentConnectionId.ToString( ), callId.ToString( ) );
        }

        AsyncServerStreamingCall<EncryptedEnvelope> call;

        if (args is not null) {
            ScriptRequest innerRequest = new( ) { Script = scriptPath };
            innerRequest.Args.AddRange( args );

            EncryptedEnvelope envelope = EncryptRequest(
                innerRequest, connection.SharedKey, keyId, agentConnectionId );

            switch (operatorType) {
                case OperatorType.PowerShell:
                    call = new Pwsh.PwshClient( channel )
                        .RunScriptWithArgs( envelope, callOptions );
                    break;
                case OperatorType.SystemShell:
                    call = new SystemShell.SystemShellClient( channel )
                        .RunScriptWithArgs( envelope, callOptions );
                    break;
                default:
                    yield return OperatorOutput.Create(
                        "Error", $"Unsupported operator type: {operatorType}" );
                    yield break;
            }
        } else {
            EncryptedEnvelope envelope = EncryptRequest(
                new ShellRequest { Command = scriptPath }, connection.SharedKey, keyId, agentConnectionId );

            switch (operatorType) {
                case OperatorType.PowerShell:
                    call = new Pwsh.PwshClient( channel )
                        .RunScript( envelope, callOptions );
                    break;
                case OperatorType.SystemShell:
                    call = new SystemShell.SystemShellClient( channel )
                        .RunScript( envelope, callOptions );
                    break;
                default:
                    yield return OperatorOutput.Create(
                        "Error", $"Unsupported operator type: {operatorType}" );
                    yield break;
            }
        }

        using (call) {
            IAsyncEnumerable<OperatorOutput> stream = GrpcOutputReader.ReadAsync(
                call.ResponseStream, connection.SharedKey, cancellationToken );

            IAsyncEnumerator<OperatorOutput> enumerator = stream.GetAsyncEnumerator( cancellationToken );
            try {
                while (true) {
                    bool moved;
                    try {
                        moved = await enumerator.MoveNextAsync( );
                    } catch (Exception ex) {
                        throw TranslateException( ex, agentConnectionId );
                    }
                    if (!moved) {
                        break;
                    }

                    yield return enumerator.Current;
                }
            } finally {
                await enumerator.DisposeAsync( );
            }
        }
    }

    /// <summary>
    /// Executes a built-in action on the specified agent and yields decrypted output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side <see cref="RegisteredConnection.Id"/>.</param>
    /// <param name="descriptor">The action descriptor containing action name and JSON parameters.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/abort.</param>
    /// <returns>An async enumerable of decrypted <see cref="OperatorOutput"/> records.</returns>
    public async IAsyncEnumerable<OperatorOutput> ExecuteActionAsync(
        Guid agentConnectionId,
        ActionDescriptor descriptor,
        [EnumeratorCancellation] CancellationToken cancellationToken = default ) {

        (Grpc.Net.Client.GrpcChannel channel, RegisteredConnection connection) =
            await ResolveChannelAsync( agentConnectionId, cancellationToken );

        string keyId = connection.ActiveKeyId ?? connection.Id.ToString( );

        ActionRequest actionRequest = new( ) {
            ActionName = descriptor.Action,
            ParametersJson = JsonSerializer.Serialize( descriptor.Parameters ),
        };

        EncryptedEnvelope envelope = EncryptRequest(
            actionRequest, connection.SharedKey, keyId, agentConnectionId );

        Guid callId = Guid.NewGuid( );
        CallOptions callOptions = AgentConnectionManager.CreateCallOptions(
            connection, callId, cancellationToken );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Dispatching Action '{ActionName}' to Agent {AgentId}, CallId {CallId}.",
                descriptor.Action, agentConnectionId.ToString( ), callId.ToString( ) );
        }

        Werkr.Agent.Protos.Action.ActionClient actionClient = new( channel );
        using AsyncServerStreamingCall<EncryptedEnvelope> call = actionClient.RunAction( envelope, callOptions );

        IAsyncEnumerable<OperatorOutput> stream = GrpcOutputReader.ReadAsync(
            call.ResponseStream, connection.SharedKey, cancellationToken );

        IAsyncEnumerator<OperatorOutput> enumerator = stream.GetAsyncEnumerator( cancellationToken );
        try {
            while (true) {
                bool moved;
                try {
                    moved = await enumerator.MoveNextAsync( );
                } catch (Exception ex) {
                    throw TranslateException( ex, agentConnectionId );
                }
                if (!moved) {
                    break;
                }

                yield return enumerator.Current;
            }
        } finally {
            await enumerator.DisposeAsync( );
        }
    }

    /// <summary>
    /// Resolves the gRPC channel for the agent, translating raw exceptions
    /// into <see cref="CommandDispatcherException"/>.
    /// </summary>
    private async Task<(Grpc.Net.Client.GrpcChannel Channel, RegisteredConnection Connection)>
        ResolveChannelAsync( Guid agentConnectionId, CancellationToken cancellationToken ) {
        try {
            return await connectionManager.GetChannelAsync( agentConnectionId, cancellationToken );
        } catch (InvalidOperationException ex) when (
            ex.Message.Contains( "not found", StringComparison.OrdinalIgnoreCase )) {
            throw new CommandDispatcherException(
                CommandDispatchFailure.AgentNotFound,
                $"Agent connection '{agentConnectionId}' was not found.",
                agentConnectionId, ex );
        } catch (InvalidOperationException ex) when (
            ex.Message.Contains( "revoked", StringComparison.OrdinalIgnoreCase )) {
            throw new CommandDispatcherException(
                CommandDispatchFailure.AgentRevoked,
                $"Agent connection '{agentConnectionId}' has been revoked.",
                agentConnectionId, ex );
        } catch (InvalidOperationException ex) {
            throw new CommandDispatcherException(
                CommandDispatchFailure.AgentNotFound,
                ex.Message,
                agentConnectionId, ex );
        }
    }

    /// <summary>
    /// Encrypts a protobuf request into an <see cref="EncryptedEnvelope"/>,
    /// wrapping cryptographic failures into a typed exception.
    /// </summary>
    private static EncryptedEnvelope EncryptRequest<T>(
        T message, byte[] sharedKey, string keyId, Guid agentConnectionId )
        where T : Google.Protobuf.IMessage<T> {
        try {
            return PayloadEncryptor.EncryptToEnvelope( message, sharedKey, keyId );
        } catch (CryptographicException ex) {
            throw new CommandDispatcherException(
                CommandDispatchFailure.EncryptionError,
                "Failed to encrypt the command payload.",
                agentConnectionId, ex );
        }
    }

    /// <summary>
    /// Translates raw exceptions into <see cref="CommandDispatcherException"/>.
    /// </summary>
    private static CommandDispatcherException TranslateException( Exception ex, Guid agentConnectionId ) =>
        ex switch {
            CommandDispatcherException cde => cde,
            RpcException rpc when rpc.StatusCode == StatusCode.Unavailable =>
                new CommandDispatcherException(
                    CommandDispatchFailure.AgentUnreachable,
                    $"Agent '{agentConnectionId}' is unreachable.",
                    agentConnectionId, rpc ),
            RpcException rpc =>
                new CommandDispatcherException(
                    CommandDispatchFailure.AgentUnreachable,
                    $"gRPC error communicating with agent '{agentConnectionId}': {rpc.Status.Detail}",
                    agentConnectionId, rpc ),
            AuthenticationException auth =>
                new CommandDispatcherException(
                    CommandDispatchFailure.TlsError,
                    "TLS authentication failed while connecting to the agent.",
                    agentConnectionId, auth ),
            CryptographicException crypto =>
                new CommandDispatcherException(
                    CommandDispatchFailure.EncryptionError,
                    "Payload decryption failed.",
                    agentConnectionId, crypto ),
            _ => new CommandDispatcherException(
                    CommandDispatchFailure.Unknown,
                    ex.Message,
                    agentConnectionId, ex )
        };
}
