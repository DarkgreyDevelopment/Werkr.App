using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>StopProcess</c> action - stops a running process by name or PID.
/// Optionally force-kills the process.
/// </summary>
public sealed class StopProcessHandler : IActionHandler {

    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<StopProcessHandler> _logger;

    /// <summary>Creates a new <see cref="StopProcessHandler"/>.</summary>
    public StopProcessHandler( ILogger<StopProcessHandler> logger ) {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Action => "StopProcess";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            StopProcessParameters p = parameters.Deserialize<StopProcessParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize StopProcess parameters." );

            if (p.ProcessId.HasValue) {
                // Stop by PID
                Process process = Process.GetProcessById( p.ProcessId.Value );
                string processName = process.ProcessName;

                if (p.Force) {
                    process.Kill( entireProcessTree: true );
                } else {
                    _ = process.CloseMainWindow( );
                }

                await output.WriteAsync(
                    OperatorOutput.Create( LogLevel.Information,
                        $"Stopped process '{processName}' (PID: {p.ProcessId.Value})" ),
                    cancellationToken );
            } else {
                // Stop by name
                Process[] processes = Process.GetProcessesByName( p.ProcessName );

                if (processes.Length == 0) {
                    await output.WriteAsync(
                        OperatorOutput.Create( LogLevel.Warning,
                            $"No processes found with name '{p.ProcessName}'." ),
                        cancellationToken );
                    return new ActionOperatorResult( Success: false );
                }

                foreach (Process process in processes) {
                    cancellationToken.ThrowIfCancellationRequested( );
                    try {
                        int pid = process.Id;
                        if (p.Force) {
                            process.Kill( entireProcessTree: true );
                        } else {
                            _ = process.CloseMainWindow( );
                        }

                        await output.WriteAsync(
                            OperatorOutput.Create( LogLevel.Information,
                                $"Stopped process '{p.ProcessName}' (PID: {pid})" ),
                            cancellationToken );
                    } finally {
                        process.Dispose( );
                    }
                }
            }

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError( ex, "StopProcess action failed" );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"StopProcess failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }
}
