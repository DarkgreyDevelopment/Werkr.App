using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using Werkr.Core.Communication;
using Werkr.Core.Operators;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake action handler that always succeeds. Used by <c>ActionOperatorTests</c>.
/// </summary>
internal sealed class SuccessHandler : IActionHandler {

    public SuccessHandler( string action = "TestAction" ) {
        Action = action;
    }

    public string Action { get; }

    public async Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        CancellationToken cancellationToken ) {
        await output.WriteAsync(
            OperatorOutput.Create( LogLevel.Information, "Success" ), cancellationToken );
        return new ActionOperatorResult( Success: true );
    }
}
