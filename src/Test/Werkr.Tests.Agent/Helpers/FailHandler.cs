using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always fails (returns Success = false, no throw).
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="FailHandler"/> class with an optional action name.
/// </remarks>
internal sealed class FailHandler( string action = "FailAction" ) : IActionHandler {

    /// <summary>
    /// Gets the action name that this handler is registered under.
    /// </summary>
    public string Action { get; } = action;

    /// <summary>
    /// Executes the handler by writing an error output and returning a failure result.
    /// </summary>
    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    ) {
        await output.WriteAsync(
            OperatorOutput.Create(
                LogLevel.Error,
                "Action failed"
            ),
            cancellationToken
        );
        return new ActionOperatorResult( Success: false );
    }
}
