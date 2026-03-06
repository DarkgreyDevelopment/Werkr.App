using System.Diagnostics;
using System.Threading.Channels;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators;

/// <summary>
/// System shell operator - executes cmd.exe (Windows) or /bin/bash (Linux/macOS) commands and scripts.
/// Captures stdout/stderr via <see cref="BoundedChannelOptions"/> with backpressure support.
/// Returns a <see cref="ShellOperatorResult"/> after execution completes.
/// </summary>
/// <remarks>Creates a new <see cref="SystemShellOperator"/>.</remarks>
/// <param name="logger">Logger for diagnostics.</param>
public class SystemShellOperator( ILogger<SystemShellOperator> logger ) : IShellOperator {

    /// <inheritdoc/>
    public bool IsAvailable => OperatingSystem.IsWindows( ) || OperatingSystem.IsLinux( ) || OperatingSystem.IsMacOS( );

    /// <inheritdoc/>
    public OperatorExecution RunCommand(
        string command,
        CancellationToken cancellationToken = default
    ) {
        Guid callId = Guid.NewGuid( );
        Channel<OperatorOutput> channel = Channel.CreateBounded<OperatorOutput>(
            new BoundedChannelOptions( 10_000 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = false } );

        TaskCompletionSource<IOperatorResult> resultTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = ExecuteCommandInternal( command, callId, channel.Writer, resultTcs, cancellationToken );
        return new OperatorExecution( channel.Reader.ReadAllAsync( cancellationToken ), resultTcs.Task );
    }

    /// <inheritdoc/>
    public OperatorExecution RunScript(
        string scriptPath,
        CancellationToken cancellationToken = default
    ) {
        if (!File.Exists( scriptPath )) {
            Guid errorCallId = Guid.NewGuid( );
            Channel<OperatorOutput> errorChannel = Channel.CreateBounded<OperatorOutput>(
                new BoundedChannelOptions( 10 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true } );
            FileNotFoundException ex = new( $"Script file not found: {scriptPath}", scriptPath );
            _ = WriteErrorAndComplete( errorChannel.Writer, errorCallId, ex.Message );
            return new OperatorExecution(
                errorChannel.Reader.ReadAllAsync( cancellationToken ),
                Task.FromResult<IOperatorResult>( new ShellOperatorResult( ExitCode: -1, Exception: ex ) ) );
        }

        return RunCommand( scriptPath, cancellationToken );
    }

    /// <inheritdoc/>
    public OperatorExecution RunScriptWithArgs(
        string scriptPath,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default
    ) {
        if (!File.Exists( scriptPath )) {
            Guid errorCallId = Guid.NewGuid( );
            Channel<OperatorOutput> errorChannel = Channel.CreateBounded<OperatorOutput>(
                new BoundedChannelOptions( 10 ) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true } );
            FileNotFoundException ex = new( $"Script file not found: {scriptPath}", scriptPath );
            _ = WriteErrorAndComplete( errorChannel.Writer, errorCallId, ex.Message );
            return new OperatorExecution(
                errorChannel.Reader.ReadAllAsync( cancellationToken ),
                Task.FromResult<IOperatorResult>( new ShellOperatorResult( ExitCode: -1, Exception: ex ) ) );
        }

        string command = $"\"{scriptPath}\" {string.Join( ' ', args )}";
        return RunCommand( command, cancellationToken );
    }

    /// <summary>
    /// Core execution loop that spawns the native shell process, captures stdout/stderr via data-received events,
    /// waits for exit, and writes the final exit code to the result.
    /// </summary>
    private async Task ExecuteCommandInternal(
        string command,
        Guid callId,
        ChannelWriter<OperatorOutput> writer,
        TaskCompletionSource<IOperatorResult> resultTcs,
        CancellationToken cancellationToken
    ) {

        try {
            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"Begin CallId: {callId}" ), cancellationToken );

            // Determine platform shell
            string shellExe;
            string shellArg;
            if (OperatingSystem.IsWindows( )) {
                shellExe = "cmd.exe";
                shellArg = "/C";
            } else {
                shellExe = "/bin/bash";
                shellArg = "-c";
            }

            using Process process = new( ) {
                StartInfo = new ProcessStartInfo {
                    FileName = shellExe,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath( )
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.ArgumentList.Add( shellArg );
            process.StartInfo.ArgumentList.Add( command );

            process.OutputDataReceived += ( sender, e ) => {
                if (e.Data is not null) {
                    _ = writer.TryWrite( OperatorOutput.Create( "Information", e.Data ) );
                }
            };

            process.ErrorDataReceived += ( sender, e ) => {
                if (e.Data is not null) {
                    _ = writer.TryWrite( OperatorOutput.Create( "Error", e.Data ) );
                }
            };

            _ = process.Start( );
            process.BeginOutputReadLine( );
            process.BeginErrorReadLine( );

            // Register cancellation callback
            using CancellationTokenRegistration registration = cancellationToken.Register( ( ) => {
                try {
                    process.Kill( entireProcessTree: true );
                } catch (InvalidOperationException) {
                    // Process may have already exited
                }
            } );

            await process.WaitForExitAsync( cancellationToken );

            int exitCode = process.ExitCode;

            if (exitCode != 0) {
                await writer.WriteAsync(
                    OperatorOutput.Create( "Error", $"Exited with code {exitCode}" ),
                    CancellationToken.None );
            }

            await writer.WriteAsync( OperatorOutput.Create( "Debug", $"End CallId: {callId}" ), CancellationToken.None );

            _ = resultTcs.TrySetResult( new ShellOperatorResult( ExitCode: exitCode ) );
        } catch (OperationCanceledException ex) {
            await writer.WriteAsync( OperatorOutput.Create( "Warning", $"Cancelled CallId: {callId}" ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new ShellOperatorResult( ExitCode: -1, Exception: ex ) );
        } catch (Exception ex) {
            logger.LogError( ex, "System shell execution failed for CallId {CallId}.", callId );
            await writer.WriteAsync( OperatorOutput.Create( "Error", ex.ToString( ) ), CancellationToken.None );
            _ = resultTcs.TrySetResult( new ShellOperatorResult( ExitCode: -1, Exception: ex ) );
        } finally {
            writer.Complete( );
        }
    }

    /// <summary>
    /// Writes a standardized error sequence (debug begin marker, error message, debug end marker)
    /// to the channel writer and then completes the channel. Used for early failures such as missing script files.
    /// </summary>
    private static async Task WriteErrorAndComplete(
        ChannelWriter<OperatorOutput> writer,
        Guid callId,
        string message
    ) {
        await writer.WriteAsync( OperatorOutput.Create( "Debug", $"Begin CallId: {callId}" ) );
        await writer.WriteAsync( OperatorOutput.Create( "Error", message ) );
        await writer.WriteAsync( OperatorOutput.Create( "Debug", $"End CallId: {callId}" ) );
        writer.Complete( );
    }
}
