using Werkr.Common.Models.Actions;

namespace Werkr.Core.Operators;

/// <summary>
/// Interface for the built-in action operator.
/// Dispatches <see cref="ActionDescriptor"/> requests to the appropriate
/// <see cref="IActionHandler"/> and returns a streaming
/// <see cref="OperatorExecution"/> result identical in shape to
/// <see cref="IShellOperator"/>, so the entire downstream pipeline
/// (output streaming, success criteria) works without changes.
/// </summary>
public interface IActionOperator {
    /// <summary>
    /// Executes a built-in action described by <paramref name="descriptor"/>.
    /// </summary>
    /// <param name="descriptor">
    /// The action descriptor containing the action name string and JSON parameters.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for timeout/cancellation support.</param>
    /// <returns>
    /// An <see cref="OperatorExecution"/> containing streamed output and a typed result.
    /// </returns>
    OperatorExecution Execute(
        ActionDescriptor descriptor,
        CancellationToken cancellationToken = default
    );
}
