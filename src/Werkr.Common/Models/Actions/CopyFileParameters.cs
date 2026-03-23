namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the CopyFile action.</summary>
public sealed record CopyFileParameters {
    /// <summary>Source file or directory path. Supports wildcard patterns.</summary>
    public required string Source { get; init; }

    /// <summary>Destination file or directory path.</summary>
    public required string Destination { get; init; }

    /// <summary>Whether to overwrite existing files at the destination.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Whether to copy directories recursively.</summary>
    public bool Recursive { get; init; }
}
