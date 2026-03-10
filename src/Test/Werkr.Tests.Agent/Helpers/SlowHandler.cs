using System.Text.Json;
using System.Threading.Channels;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that delays forever (until cancelled).
/// Used for timeout and cancellation tests.
/// </summary>
internal sealed class SlowHandler : IActionHandler {

    /// <summary>
    /// Initializes a new instance of the <see cref="SlowHandler"/> class with an optional action name.
    /// </summary>
    public SlowHandler( string action = "SlowAction" ) {
        Action = action;
    }

    /// <summary>
    /// Gets the action name that this handler is registered under.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Executes the handler by writing a start message and then blocking
    /// indefinitely. The method will only return if the
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        await output.WriteAsync(
            OperatorOutput.Create(
                LogLevel.Information,
                "Starting slow action..."
            ),
            cancellationToken
        );
        // Wait indefinitely until cancelled
        await Task.Delay(
            Timeout.Infinite,
            cancellationToken
        );
        return new ActionOperatorResult( Success: true );
    }
}
