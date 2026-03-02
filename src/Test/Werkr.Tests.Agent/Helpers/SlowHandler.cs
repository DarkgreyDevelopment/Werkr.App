using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that delays forever (until cancelled).
/// Used for timeout and cancellation tests.
/// </summary>
internal sealed class SlowHandler : IActionHandler {

    public SlowHandler( string action = "SlowAction" ) {
        Action = action;
    }

    public string Action { get; }

    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        await output.WriteAsync(
            OperatorOutput.Create( LogLevel.Information, "Starting slow action..." ), cancellationToken );
        // Wait indefinitely until cancelled
        await Task.Delay( Timeout.Infinite, cancellationToken );
        return new ActionOperatorResult( Success: true );
    }
}
