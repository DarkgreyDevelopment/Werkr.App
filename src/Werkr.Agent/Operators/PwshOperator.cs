using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Channels;

using Microsoft.Extensions.Options;

using Werkr.Common.Configuration;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators;

/// <summary>
/// PowerShell operator — executes PowerShell commands and scripts using the PowerShell SDK.
/// Uses a custom <see cref="WerkrPSHost"/> to route all output (formatting cmdlets,
/// <c>Write-Host</c>, <c>Write-Error</c>, etc.) through a single
/// <see cref="ChannelWriter{T}"/> path. A new <see cref="Runspace"/> is created per
/// invocation for clean isolation.
/// </summary>
/// <remarks>Creates a new <see cref="PwshOperator"/>.</remarks>
/// <param name="agentSettingsOptions">Agent settings containing PowerShell configuration.</param>
/// <param name="logger">Logger for diagnostics.</param>
public class PwshOperator(
    IOptions<AgentSettings> agentSettingsOptions,
    ILogger<PwshOperator> logger ) : IShellOperator {

    private readonly int _bufferWidth = agentSettingsOptions.Value.PowerShell.BufferWidth;

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public OperatorExecution RunCommand( string command, CancellationToken cancellationToken = default ) {
        Guid callId = Guid.NewGuid( );
        Channel<OperatorOutput> channel = Channel.CreateBounded<OperatorOutput>(
            new BoundedChannelOptions( 10_000 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false } );

        TaskCompletionSource<IOperatorResult> resultTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = ExecuteCommandInternal( command, callId, channel.Writer, resultTcs, cancellationToken );
        return new OperatorExecution( channel.Reader.ReadAllAsync( cancellationToken ), resultTcs.Task );
    }

    /// <inheritdoc/>
    public OperatorExecution RunScript( string scriptPath, CancellationToken cancellationToken = default ) {
        if (!File.Exists( scriptPath )) {
            Guid errorCallId = Guid.NewGuid( );
            Channel<OperatorOutput> errorChannel = Channel.CreateBounded<OperatorOutput>(
                new BoundedChannelOptions( 10 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true } );
            FileNotFoundException ex = new( $"Script file not found: {scriptPath}", scriptPath );
            _ = WriteErrorAndComplete( errorChannel.Writer, errorCallId, ex.Message );
            return new OperatorExecution(
                errorChannel.Reader.ReadAllAsync( cancellationToken ),
                Task.FromResult<IOperatorResult>( new PwshOperatorResult( HadErrors: true, LastExitCode: null, Exception: ex ) ) );
        }

        string script = File.ReadAllText( scriptPath );
        return RunCommand( script, cancellationToken );
    }

    /// <inheritdoc/>
    public OperatorExecution RunScriptWithArgs( string scriptPath, IEnumerable<string> args, CancellationToken cancellationToken = default ) {
        if (!File.Exists( scriptPath )) {
            Guid errorCallId = Guid.NewGuid( );
            Channel<OperatorOutput> errorChannel = Channel.CreateBounded<OperatorOutput>(
                new BoundedChannelOptions( 10 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true } );
            FileNotFoundException ex = new( $"Script file not found: {scriptPath}", scriptPath );
            _ = WriteErrorAndComplete( errorChannel.Writer, errorCallId, ex.Message );
            return new OperatorExecution(
                errorChannel.Reader.ReadAllAsync( cancellationToken ),
                Task.FromResult<IOperatorResult>( new PwshOperatorResult( HadErrors: true, LastExitCode: null, Exception: ex ) ) );
        }

        Guid callId = Guid.NewGuid( );
        Channel<OperatorOutput> channel = Channel.CreateBounded<OperatorOutput>(
            new BoundedChannelOptions( 10_000 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false } );

        TaskCompletionSource<IOperatorResult> resultTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = ExecuteScriptWithArgsInternal( scriptPath, args, callId, channel.Writer, resultTcs, cancellationToken );
        return new OperatorExecution( channel.Reader.ReadAllAsync( cancellationToken ), resultTcs.Task );
    }

    private async Task ExecuteCommandInternal(
        string command,
        Guid callId,
        ChannelWriter<OperatorOutput> writer,
        TaskCompletionSource<IOperatorResult> resultTcs,
        CancellationToken cancellationToken ) {

        Runspace? runspace = null;
        try {
            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"Begin CallId: {callId}" ), cancellationToken );

            WerkrPSHost host = new( writer, _bufferWidth );
            runspace = RunspaceFactory.CreateRunspace( host, InitialSessionState.CreateDefault( ) );
            runspace.Open( );

            using PowerShell pwsh = PowerShell.Create( );
            pwsh.Runspace = runspace;

            // AddScript + Out-Default routes ALL output through the custom PSHost UI.
            // The PSDataCollection<PSObject> results will be empty — everything flows
            // through WerkrPSHostUserInterface → ChannelWriter.
            _ = pwsh.AddScript( command, useLocalScope: true );
            _ = pwsh.AddCommand( "Out-Default" );

            // Register cancellation callback
            using CancellationTokenRegistration registration = cancellationToken.Register( ( ) => pwsh.Stop( ) );

            // Invoke — all output is routed through the host UI
            _ = await pwsh.InvokeAsync( ).WaitAsync( cancellationToken );

            // Non-terminating errors (Write-Error) accumulate in the error stream
            // but are NOT rendered through the host UI in SDK InvokeAsync context.
            // Capture them explicitly post-invocation.
            if (pwsh.HadErrors) {
                foreach (ErrorRecord error in pwsh.Streams.Error) {
                    await writer.WriteAsync(
                        OperatorOutput.Create( "Error", FormatErrorRecord( error ) ),
                        cancellationToken );
                }
            }

            // Extract $LASTEXITCODE if available
            int? lastExitCode = ExtractLastExitCode( pwsh );

            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"End CallId: {callId}" ), cancellationToken );

            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: pwsh.HadErrors,
                LastExitCode: lastExitCode ) );
        } catch (OperationCanceledException ex) {
            await writer.WriteAsync( OperatorOutput.Create( "Warning", $"Cancelled CallId: {callId}" ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: true,
                LastExitCode: null,
                Exception: ex ) );
        } catch (Exception ex) {
            logger.LogError( ex, "PowerShell execution failed for CallId {CallId}.", callId );
            await writer.WriteAsync( OperatorOutput.Create( "Error", ex.ToString( ) ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: true,
                LastExitCode: null,
                Exception: ex ) );
        } finally {
            runspace?.Dispose( );
            writer.Complete( );
        }
    }

    private async Task ExecuteScriptWithArgsInternal(
        string scriptPath,
        IEnumerable<string> args,
        Guid callId,
        ChannelWriter<OperatorOutput> writer,
        TaskCompletionSource<IOperatorResult> resultTcs,
        CancellationToken cancellationToken ) {

        Runspace? runspace = null;
        try {
            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"Begin CallId: {callId}" ), cancellationToken );

            string script = await File.ReadAllTextAsync( scriptPath, cancellationToken );

            WerkrPSHost host = new( writer, _bufferWidth );
            runspace = RunspaceFactory.CreateRunspace( host, InitialSessionState.CreateDefault( ) );
            runspace.Open( );

            using PowerShell pwsh = PowerShell.Create( );
            pwsh.Runspace = runspace;

            _ = pwsh.AddScript( script, useLocalScope: true );
            _ = pwsh.AddParameters( args.ToArray( ) );
            _ = pwsh.AddCommand( "Out-Default" );

            using CancellationTokenRegistration registration = cancellationToken.Register( ( ) => pwsh.Stop( ) );

            // Invoke — all output is routed through the host UI
            _ = await pwsh.InvokeAsync( ).WaitAsync( cancellationToken );

            // Non-terminating errors — capture from error stream post-invocation
            if (pwsh.HadErrors) {
                foreach (ErrorRecord error in pwsh.Streams.Error) {
                    await writer.WriteAsync(
                        OperatorOutput.Create( "Error", FormatErrorRecord( error ) ),
                        cancellationToken );
                }
            }

            int? lastExitCode = ExtractLastExitCode( pwsh );

            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"End CallId: {callId}" ), cancellationToken );

            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: pwsh.HadErrors,
                LastExitCode: lastExitCode ) );
        } catch (OperationCanceledException ex) {
            await writer.WriteAsync( OperatorOutput.Create( "Warning", $"Cancelled CallId: {callId}" ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: true,
                LastExitCode: null,
                Exception: ex ) );
        } catch (Exception ex) {
            logger.LogError( ex, "PowerShell script execution failed for CallId {CallId}.", callId );
            await writer.WriteAsync( OperatorOutput.Create( "Error", ex.ToString( ) ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new PwshOperatorResult(
                HadErrors: true,
                LastExitCode: null,
                Exception: ex ) );
        } finally {
            runspace?.Dispose( );
            writer.Complete( );
        }
    }

    /// <summary>
    /// Extracts the <c>$LASTEXITCODE</c> variable from the PowerShell session, if set.
    /// Returns null when no native command was invoked.
    /// </summary>
    private static int? ExtractLastExitCode( PowerShell pwsh ) {
        try {
            object? lastExitCodeObj = pwsh.Runspace.SessionStateProxy
                .GetVariable( "LASTEXITCODE" );
            if (lastExitCodeObj is int code) {
                return code;
            }
        } catch {
            // Runspace may be closed or unavailable
        }
        return null;
    }

    private static string FormatErrorRecord( ErrorRecord error ) {
        StringBuilder sb = new( );
        _ = sb.AppendLine( error.Exception?.Message ?? "Unknown error" );
        if (error.InvocationInfo is not null) {
            _ = sb.AppendLine( $"  Script: {error.InvocationInfo.ScriptName}" );
            _ = sb.AppendLine( $"  Line: {error.InvocationInfo.ScriptLineNumber}" );
            _ = sb.AppendLine( $"  Position: {error.InvocationInfo.PositionMessage}" );
        }

        if (error.CategoryInfo is not null) {
            _ = sb.AppendLine( $"  Category: {error.CategoryInfo.Category}" );
        }

        return sb.ToString( ).TrimEnd( );
    }

    private static async Task WriteErrorAndComplete( ChannelWriter<OperatorOutput> writer, Guid callId, string message ) {
        await writer.WriteAsync( OperatorOutput.Create( "Debug", $"Begin CallId: {callId}" ) );
        await writer.WriteAsync( OperatorOutput.Create( "Error", message ) );
        await writer.WriteAsync( OperatorOutput.Create( "Debug", $"End CallId: {callId}" ) );
        writer.Complete( );
    }
}
