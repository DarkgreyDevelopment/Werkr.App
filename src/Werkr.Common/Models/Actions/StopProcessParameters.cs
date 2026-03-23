namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the StopProcess action.</summary>
public sealed record StopProcessParameters {
    /// <summary>Name of the process to stop.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Optional specific process ID. When set, only this PID is stopped.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Whether to forcefully terminate the process.</summary>
    public bool Force { get; init; }
}
