namespace Werkr.Common.Models;

/// <summary>
/// A single line of operator output from a command or script execution.
/// Mirrors the shape of <c>OperatorOutput</c> in the Core project for JSON transport.
/// </summary>
/// <param name="LogLevel">The severity level (e.g., Trace, Debug, Information, Warning, Error).</param>
/// <param name="Message">The output content.</param>
/// <param name="Timestamp">ISO 8601 UTC timestamp.</param>
public sealed record OperatorOutputLine(
    string LogLevel,
    string Message,
    string Timestamp );
