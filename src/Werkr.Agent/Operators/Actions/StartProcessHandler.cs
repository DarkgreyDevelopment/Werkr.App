using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;
using Werkr.Core.Security;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>StartProcess</c> action — starts an external process.
/// Optionally waits for the process to exit with an optional timeout.
/// </summary>
public sealed class StartProcessHandler : IActionHandler {

    private readonly IFilePathResolver _resolver;
    private readonly ILogger<StartProcessHandler> _logger;

    /// <summary>Creates a new <see cref="StartProcessHandler"/>.</summary>
    public StartProcessHandler( IFilePathResolver resolver, ILogger<StartProcessHandler> logger ) {
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "StartProcess";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        try {
            StartProcessParameters p = parameters.Deserialize<StartProcessParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize StartProcess parameters." );

            // Validate FileName only when it's a rooted path. Bare executable
            // names (e.g. "dotnet") are resolved by the OS via PATH lookup, not
            // by Path.GetFullPath. When allowlist enforcement is enabled, bare
            // names bypass path validation — OS-level PATH security is the
            // appropriate control for non-rooted executables.
            if (Path.IsPathRooted( p.FileName )) {
                _ = _resolver.ResolveSinglePath( p.FileName );
            }

            if (p.WorkingDirectory != null) {
                _ = _resolver.ResolveSinglePath( p.WorkingDirectory );
            }

            ProcessStartInfo startInfo = new( ) {
                FileName = p.FileName,
                Arguments = p.Arguments ?? string.Empty,
                WorkingDirectory = p.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using Process process = new( ) { StartInfo = startInfo, EnableRaisingEvents = true };

            // Event-based async read pattern — prevents deadlock when the
            // process produces more output than the pipe buffer (~4KB).
            // Matches the proven SystemShellOperator pattern.
            process.OutputDataReceived += ( _, e ) => {
                if (e.Data is not null) {
                    _ = output.TryWrite( OperatorOutput.Create( LogLevel.Information, e.Data ) );
                }
            };

            process.ErrorDataReceived += ( _, e ) => {
                if (e.Data is not null) {
                    _ = output.TryWrite( OperatorOutput.Create( LogLevel.Error, e.Data ) );
                }
            };

            if (!process.Start( )) {
                throw new InvalidOperationException( $"Failed to start process '{p.FileName}'." );
            }

            process.BeginOutputReadLine( );
            process.BeginErrorReadLine( );

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, $"Started process '{p.FileName}' (PID: {process.Id})" ),
                cancellationToken );

            if (p.WaitForExit) {
                if (p.TimeoutMs.HasValue) {
                    bool exited = await WaitForExitWithTimeout( process, p.TimeoutMs.Value, cancellationToken );
                    if (!exited) {
                        process.Kill( entireProcessTree: true );
                        await output.WriteAsync(
                            OperatorOutput.Create( LogLevel.Warning,
                                $"Process '{p.FileName}' (PID: {process.Id}) timed out after {p.TimeoutMs}ms and was killed." ),
                            cancellationToken );
                        return new ActionOperatorResult( Success: false );
                    }
                } else {
                    await process.WaitForExitAsync( cancellationToken );
                }

                // Synchronous WaitForExit() (no arguments) ensures all buffered
                // OutputDataReceived/ErrorDataReceived events have been delivered.
                // WaitForExitAsync returns when the process exits, but async
                // output events may still be in-flight.
                process.WaitForExit( );

                int exitCode = process.ExitCode;
                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information, $"Process exited with code {exitCode}" ),
                    cancellationToken );

                return new ActionOperatorResult( Success: exitCode == 0 );
            }

            // Fire and forget — process started but not awaited
            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "StartProcess action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"StartProcess failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    private static async Task<bool> WaitForExitWithTimeout(
        Process process, int timeoutMs, CancellationToken cancellationToken ) {
        using CancellationTokenSource timeoutCts = new( timeoutMs );
        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, timeoutCts.Token );

        try {
            await process.WaitForExitAsync( linkedCts.Token );
            return true;
        } catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
            return false;
        }
    }
}
