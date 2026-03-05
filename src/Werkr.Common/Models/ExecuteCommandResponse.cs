namespace Werkr.Common.Models;

/// <summary>Response DTO for the command execution endpoint.</summary>
/// <param name="Success">Whether the execution completed without error.</param>
/// <param name="Output">The collected operator output lines.</param>
/// <param name="Error">Optional error message when <paramref name="Success"/> is <see langword="false"/>.</param>
public sealed record ExecuteCommandResponse(
    bool Success,
    List<OperatorOutputLine> Output,
    string? Error = null
);
