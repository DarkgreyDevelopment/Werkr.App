namespace Werkr.Data.Entities.Tasks;

/// <summary>
/// Actions that can be performed by a task.
/// </summary>
public enum TaskActionType {

    /// <summary>Execute a PowerShell command.</summary>
    PowerShellCommand = 0,

    /// <summary>Execute a PowerShell script.</summary>
    PowerShellScript = 1,

    /// <summary>Execute a system shell command.</summary>
    ShellCommand = 2,

    /// <summary>Execute a system shell script.</summary>
    ShellScript = 3,

    /// <summary>Built-in file/process action.</summary>
    Action = 4,
}
