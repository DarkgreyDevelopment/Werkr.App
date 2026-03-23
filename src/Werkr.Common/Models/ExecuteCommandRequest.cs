namespace Werkr.Common.Models;

/// <summary>Request body for the agent execute/shell command endpoint.</summary>
/// <param name="Command">The shell command or script content to execute.</param>
/// <param name="ActionType">
/// Optional action type as an integer matching <c>TaskActionType</c> values:
/// 0 = PowerShellCommand, 1 = PowerShellScript, 2 = ShellCommand, 3 = ShellScript, 4 = Action.
/// Defaults to 2 (ShellCommand) when omitted.
/// </param>
public sealed record ExecuteCommandRequest(
    string Command,
    int? ActionType = null
);
