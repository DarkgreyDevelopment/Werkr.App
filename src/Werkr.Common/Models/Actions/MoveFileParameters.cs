namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the MoveFile action.</summary>
public sealed record MoveFileParameters {
    /// <summary>Source file or directory path. Supports wildcard patterns.</summary>
    public required string Source { get; init; }

    /// <summary>Destination file or directory path.</summary>
    public required string Destination { get; init; }

    /// <summary>Whether to overwrite existing files at the destination.</summary>
    public bool Overwrite { get; init; }
}
