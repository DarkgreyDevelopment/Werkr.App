using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always fails (returns Success = false, no throw).
/// </summary>
internal sealed class FailHandler : IActionHandler {

    public FailHandler( string action = "FailAction" ) {
        Action = action;
    }

    public string Action { get; }

    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        await output.WriteAsync(
            OperatorOutput.Create( LogLevel.Error, "Action failed" ), cancellationToken );
        return new ActionOperatorResult( Success: false );
    }
}
