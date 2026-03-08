using Werkr.Common.Models;
using Werkr.Common.Models.Actions;

namespace Werkr.Core.Communication;

/// <summary>
/// Dispatches commands and scripts to registered Agents and streams output.
/// </summary>
public interface ICommandDispatcher {
    /// <summary>
    /// Executes a command on the specified agent and yields output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side agent connection id.</param>
    /// <param name="operatorType">The operator type to execute with.</param>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancellation token for timeout or cancellation.</param>
    /// <returns>Streamed command output.</returns>
    IAsyncEnumerable<OperatorOutput> ExecuteCommandAsync(
        Guid agentConnectionId,
        OperatorType operatorType,
        string command,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Executes a script on the specified agent and yields output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side agent connection id.</param>
    /// <param name="operatorType">The operator type to execute with.</param>
    /// <param name="scriptPath">The script path to execute.</param>
    /// <param name="args">Optional script arguments.</param>
    /// <param name="cancellationToken">Cancellation token for timeout or cancellation.</param>
    /// <returns>Streamed script output.</returns>
    IAsyncEnumerable<OperatorOutput> ExecuteScriptAsync(
        Guid agentConnectionId,
        OperatorType operatorType,
        string scriptPath,
        IEnumerable<string>? args,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Executes a built-in action on the specified agent and yields output.
    /// </summary>
    /// <param name="agentConnectionId">The Server-side agent connection id.</param>
    /// <param name="descriptor">The action descriptor containing action name and parameters.</param>
    /// <param name="cancellationToken">Cancellation token for timeout or cancellation.</param>
    /// <returns>Streamed action output.</returns>
    IAsyncEnumerable<OperatorOutput> ExecuteActionAsync(
        Guid agentConnectionId,
        ActionDescriptor descriptor,
        CancellationToken cancellationToken = default
    );
}
