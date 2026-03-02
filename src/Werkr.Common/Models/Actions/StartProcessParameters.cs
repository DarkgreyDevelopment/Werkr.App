namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the StartProcess action.</summary>
public sealed record StartProcessParameters {
    /// <summary>Path to the executable or command to start.</summary>
    public required string FileName { get; init; }

    /// <summary>Optional command-line arguments.</summary>
    public string? Arguments { get; init; }

    /// <summary>Optional working directory for the process.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Whether to wait for the process to exit before completing.</summary>
    public bool WaitForExit { get; init; }

    /// <summary>Timeout in milliseconds when <see cref="WaitForExit"/> is true. Null means wait indefinitely.</summary>
    public int? TimeoutMs { get; init; }
}
