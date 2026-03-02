using Microsoft.Extensions.Logging;

namespace Werkr.Core.Communication;

/// <summary>
/// Transport-agnostic output record from an operator execution.
/// Maps to <c>GrpcLogMsg</c> for gRPC transport and to SignalR messages for browser delivery.
/// </summary>
/// <param name="LogLevel">The severity level of the output line (e.g., Trace, Debug, Information, Warning, Error).</param>
/// <param name="Message">The output content.</param>
/// <param name="Timestamp">ISO 8601 UTC timestamp of when the output was produced.</param>
public sealed record OperatorOutput(
    string LogLevel,
    string Message,
    string Timestamp ) {

    /// <summary>Creates an <see cref="OperatorOutput"/> with the current UTC timestamp.</summary>
    /// <param name="logLevel">The severity level.</param>
    /// <param name="message">The output content.</param>
    /// <returns>A new <see cref="OperatorOutput"/> instance.</returns>
    public static OperatorOutput Create( string logLevel, string message ) =>
        new( logLevel, message, DateTime.UtcNow.ToString( "o" ) );

    /// <summary>Creates an <see cref="OperatorOutput"/> with the current UTC timestamp.</summary>
    /// <param name="logLevel">The severity level.</param>
    /// <param name="message">The output content.</param>
    /// <returns>A new <see cref="OperatorOutput"/> instance.</returns>
    public static OperatorOutput Create( LogLevel logLevel, string message ) =>
        Create( logLevel.ToString( ), message );
}
