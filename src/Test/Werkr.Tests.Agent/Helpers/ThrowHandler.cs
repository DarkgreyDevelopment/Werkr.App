using System.Text.Json;
using System.Threading.Channels;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always throws an exception.
/// </summary>
internal sealed class ThrowHandler : IActionHandler {

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrowHandler"/> class with an optional action name.
    /// </summary>
    public ThrowHandler( string action = "ThrowAction" ) {
        Action = action;
    }

    /// <summary>
    /// Gets the action name that this handler is registered under.
    /// </summary>
    public string Action { get; }

    /// <summary>
    /// Always throws an <see cref="InvalidOperationException"/> to simulate an unexpected handler failure.
    /// </summary>
    public Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken
    ) {
        throw new InvalidOperationException( "Simulated handler failure." );
    }
}
