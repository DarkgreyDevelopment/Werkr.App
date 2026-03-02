namespace Werkr.Core.Operators;

/// <summary>
/// Interface for shell-based operators (PowerShell and system shell).
/// All methods return <see cref="OperatorExecution"/> for transport-agnostic streaming
/// with a typed result available after the stream completes.
/// Implementations live in <c>Werkr.Agent</c>; this interface lives in <c>Werkr.Core</c> for cross-project accessibility.
/// </summary>
public interface IShellOperator {
    /// <summary>Runs a single command string and yields output as it becomes available.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/cancellation support.</param>
    /// <returns>An <see cref="OperatorExecution"/> containing streamed output and a typed result.</returns>
    OperatorExecution RunCommand( string command, CancellationToken cancellationToken = default );

    /// <summary>Runs a script file and yields output as it becomes available.</summary>
    /// <param name="scriptPath">Path to the script file.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/cancellation support.</param>
    /// <returns>An <see cref="OperatorExecution"/> containing streamed output and a typed result.</returns>
    OperatorExecution RunScript( string scriptPath, CancellationToken cancellationToken = default );

    /// <summary>Runs a script file with arguments and yields output as it becomes available.</summary>
    /// <param name="scriptPath">Path to the script file.</param>
    /// <param name="args">Arguments to pass to the script.</param>
    /// <param name="cancellationToken">Cancellation token for timeout/cancellation support.</param>
    /// <returns>An <see cref="OperatorExecution"/> containing streamed output and a typed result.</returns>
    OperatorExecution RunScriptWithArgs( string scriptPath, IEnumerable<string> args, CancellationToken cancellationToken = default );

    /// <summary>Indicates whether this operator is available on the current platform.</summary>
    bool IsAvailable { get; }
}
