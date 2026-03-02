namespace Werkr.Common.Models;

/// <summary>Request DTO for the command execution endpoint.</summary>
/// <param name="OperatorType">The operator type string ("PowerShell" or "SystemShell").</param>
/// <param name="Command">The plaintext command to execute.</param>
/// <param name="TimeoutMinutes">Timeout in minutes. Defaults to 30.</param>
public sealed record ExecuteCommandRequest(
    string OperatorType,
    string Command,
    int TimeoutMinutes = 30 );
