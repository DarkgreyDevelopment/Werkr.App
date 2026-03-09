namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the WatchFile action.</summary>
public sealed record WatchFileParameters {
    /// <summary>
    /// Full path of the directory to monitor for file changes.
    /// </summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Glob pattern to match filenames (e.g. "*.csv", "report_*.xlsx").
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Number of seconds a file's size and last-write time must remain
    /// unchanged before it is considered stable. Default: 5.
    /// </summary>
    public int StabilitySeconds { get; init; } = 5;

    /// <summary>
    /// Maximum number of seconds to wait for a matching file. Default: 300 (5 minutes).
    /// </summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// Interval in milliseconds between polling checks. Default: 1000.
    /// </summary>
    public int PollIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Behavior when the timeout expires without detecting a stable file.
    /// Default: FailOnTimeout.
    /// </summary>
    public WatchFileMode Mode { get; init; } = WatchFileMode.FailOnTimeout;

    /// <summary>
    /// When true, uses polling instead of <see cref="FileSystemWatcher"/>.
    /// Recommended for network/UNC paths.
    /// </summary>
    public bool UsePolling { get; init; }
}
