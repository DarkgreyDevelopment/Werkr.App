using System.Text.Json;
using System.Threading.Channels;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always throws an exception.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ThrowHandler"/> class with an optional action name.
/// </remarks>
internal sealed class ThrowHandler( string action = "ThrowAction" ) : IActionHandler {

    /// <summary>
    /// Gets the action name that this handler is registered under.
    /// </summary>
    public string Action { get; } = action;

    /// <summary>
    /// Always throws an <see cref="InvalidOperationException"/> to simulate an unexpected handler failure.
    /// </summary>
    public Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        throw new InvalidOperationException( "Simulated handler failure." );
    }
}
