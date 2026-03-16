namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ShellCommand action.</summary>
public sealed record ShellCommandParameters {
    /// <summary>The shell command to execute.</summary>
    public required string Content { get; init; }

    /// <summary>Timeout in minutes. Defaults to 30.</summary>
    public long TimeoutMinutes { get; init; } = 30;
}
