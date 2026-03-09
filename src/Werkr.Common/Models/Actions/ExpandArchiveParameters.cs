namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ExpandArchive action.</summary>
public sealed record ExpandArchiveParameters {
    /// <summary>Full path to the archive file to extract.</summary>
    public required string Source { get; init; }

    /// <summary>Destination directory for extracted files.</summary>
    public required string Destination { get; init; }

    /// <summary>Whether to overwrite existing files during extraction.</summary>
    public bool Overwrite { get; init; }

    /// <summary>
    /// Archive format. Default: Auto (detects from file extension).
    /// </summary>
    public ArchiveFormat Format { get; init; } = ArchiveFormat.Auto;
}
