using System.Text.Json;
using System.Threading.Channels;

using Werkr.Core.Communication;

namespace Werkr.Core.Operators;

/// <summary>
/// Interface for individual action handler implementations.
/// Each handler is responsible for a single action type identified
/// by the <see cref="Action"/> string property. The
/// action operator implementation uses this property to build a
/// string-keyed handler registry at construction time.
/// </summary>
public interface IActionHandler {
    /// <summary>
    /// The action name this handler serves (e.g. <c>"CopyFile"</c>, <c>"StartProcess"</c>).
    /// Must be unique across all registered handlers — duplicates are caught at DI construction time.
    /// </summary>
    string Action { get; }

    /// <summary>
    /// Executes the action with the given JSON parameters, writing progress
    /// and status messages to <paramref name="output"/> as the operation proceeds.
    /// </summary>
    /// <param name="parameters">
    /// JSON element containing the action-specific parameter record.
    /// The handler deserializes this into its own typed parameter record.
    /// </param>
    /// <param name="output">
    /// Channel writer for streaming structured output lines back to the caller.
    /// </param>
    /// <param name="inputVariableValue">
    /// Optional JSON blob from the step's input variable, or <see langword="null"/> if no input is declared.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for timeout/cancellation support.</param>
    /// <returns>
    /// An <see cref="ActionOperatorResult"/> indicating success or failure.
    /// </returns>
    Task<ActionOperatorResult> ExecuteAsync(
        JsonElement parameters,
        ChannelWriter<OperatorOutput> output,
        string? inputVariableValue = null,
        CancellationToken cancellationToken = default
    );
}
