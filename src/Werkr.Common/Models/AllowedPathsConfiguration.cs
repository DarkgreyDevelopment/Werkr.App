namespace Werkr.Common.Models;

/// <summary>
/// Configuration model for the per-agent path allowlist.
/// Bound from agent configuration and optionally synced from the server
/// via the ScheduleSync payload.
/// </summary>
public sealed class AllowedPathsConfiguration {
    /// <summary>Configuration section name for options binding.</summary>
    public const string SectionName = "AllowedPaths";

    /// <summary>
    /// List of permitted filesystem path prefixes (e.g. <c>["/data", "/home/werkr"]</c>).
    /// All action handler file operations are checked against these prefixes
    /// when <see cref="EnforceAllowlist"/> is <see langword="true"/>.
    /// </summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/>, all file/content/process action handlers validate paths
    /// against <see cref="Paths"/> before executing. When <see langword="false"/> (default),
    /// all paths are permitted for backward compatibility.
    /// </summary>
    public bool EnforceAllowlist { get; set; }
}
