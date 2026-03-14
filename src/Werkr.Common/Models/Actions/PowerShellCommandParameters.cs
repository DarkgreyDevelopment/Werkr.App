namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the PowerShellCommand action.</summary>
public sealed record PowerShellCommandParameters {
    /// <summary>The PowerShell command or script block to execute.</summary>
    public required string Content { get; init; }

    /// <summary>Timeout in minutes. Defaults to 30.</summary>
    public long TimeoutMinutes { get; init; } = 30;
}
