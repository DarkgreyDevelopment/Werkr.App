namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the PowerShellScript action.</summary>
public sealed record PowerShellScriptParameters {
    /// <summary>Path to the PowerShell script (.ps1) to execute.</summary>
    public required string ScriptPath { get; init; }

    /// <summary>Optional arguments to pass to the script.</summary>
    public string? Arguments { get; init; }

    /// <summary>Timeout in minutes. Defaults to 30.</summary>
    public long TimeoutMinutes { get; init; } = 30;
}
