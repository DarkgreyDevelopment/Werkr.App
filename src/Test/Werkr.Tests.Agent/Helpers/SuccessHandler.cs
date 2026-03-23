using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always succeeds. Used by <see cref="Werkr.Tests.Agent.Operators.ActionOperatorTests"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SuccessHandler"/> class with an optional action name.
/// </remarks>
internal sealed class SuccessHandler( string action = "TestAction" ) : IActionHandler {

    /// <summary>
    /// Gets the action name that this handler is registered under.
    /// </summary>
    public string Action { get; } = action;

    /// <summary>
    /// Executes the handler by writing a success output and returning a successful result.
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
                "Success"
            ),
            cancellationToken
        );
        return new ActionOperatorResult( Success: true );
    }
}
