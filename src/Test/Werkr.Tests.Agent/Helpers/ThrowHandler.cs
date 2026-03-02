using System.Text.Json;
using System.Threading.Channels;

using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always throws an exception.
/// </summary>
internal sealed class ThrowHandler : IActionHandler {

    public ThrowHandler( string action = "ThrowAction" ) {
        Action = action;
    }

    public string Action { get; }

    public Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        throw new InvalidOperationException( "Simulated handler failure." );
    }
}
