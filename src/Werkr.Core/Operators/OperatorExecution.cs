using Werkr.Core.Communication;

namespace Werkr.Core.Operators;

/// <summary>
/// The result of a shell operator execution, containing both the streamed output
/// and the final typed result with exit code / error information.
/// </summary>
/// <param name="Output">Asynchronous stream of operator output lines.</param>
/// <param name="Result">
/// A task that completes when execution finishes, providing the typed result.
/// Callers should consume <paramref name="Output"/> first, then await <paramref name="Result"/>.
/// </param>
public sealed record OperatorExecution(
    IAsyncEnumerable<OperatorOutput> Output,
    Task<IOperatorResult> Result
);
