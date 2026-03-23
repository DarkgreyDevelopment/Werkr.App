using System.Text.Json;
using System.Threading.Channels;
using Werkr.Common.Attributes;
using Werkr.Common.Models.Actions;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Agent.Operators.Actions;

/// <summary>
/// Handles the <c>Delay</c> action - pauses workflow execution for a specified duration.
/// Uses <see cref="TimeProvider"/> for testable time-dependent logic.
/// </summary>
/// <remarks>Creates a new <see cref="DelayHandler"/>.</remarks>
[ActionCategory( "ControlFlow" )]
public sealed partial class DelayHandler( ILogger<DelayHandler> logger, TimeProvider timeProvider ) : IActionHandler {

    /// <summary>
    /// Logger for recording execution errors for this handler.
    /// </summary>
    private readonly ILogger<DelayHandler> _logger = logger;
    /// <summary>
    /// Time provider for testable delay logic.
    /// </summary>
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc/>
    public string Action => "Delay";

    /// <inheritdoc/>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        try {
            DelayParameters p = parameters.Deserialize<DelayParameters>( ActionJson.SerializerOptions )
                ?? throw new ArgumentException( "Failed to deserialize Delay parameters." );

            if (p.Seconds < 0) {
                throw new ArgumentException( "Seconds must be a non-negative value." );
            }

            string reasonMessage = string.IsNullOrWhiteSpace( p.Reason )
                ? $"Delaying for {p.Seconds} second(s)..."
                : $"Delaying for {p.Seconds} second(s): {p.Reason}";

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, reasonMessage ),
                cancellationToken );

            await Task.Delay( TimeSpan.FromSeconds( p.Seconds ), _timeProvider, cancellationToken );

            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Information, "Delay completed." ),
                cancellationToken );

            return new ActionOperatorResult( Success: true );
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogActionFailed( _logger, ex );
            await output.WriteAsync(
                OperatorOutput.Create( LogLevel.Error, $"Delay failed: {ex.Message}" ),
                cancellationToken );
            return new ActionOperatorResult( Success: false, Exception: ex );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "Action failed" )]
    private static partial void LogActionFailed( ILogger logger, Exception ex );
}
