namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the CompressArchive action.</summary>
public sealed record CompressArchiveParameters {
    /// <summary>Source file path, directory path, or glob pattern to compress.</summary>
    public required string Source { get; init; }

    /// <summary>Destination path for the archive file.</summary>
    public required string Destination { get; init; }

    /// <summary>Archive format. Default: Zip.</summary>
    public ArchiveFormat Format { get; init; } = ArchiveFormat.Zip;

    /// <summary>Compression level. Default: Optimal.</summary>
    public ArchiveCompressionLevel CompressionLevel { get; init; } = ArchiveCompressionLevel.Optimal;

    /// <summary>Whether to include the base directory name inside the archive.</summary>
    public bool IncludeBaseDirectory { get; init; }

    /// <summary>Whether to overwrite an existing archive file.</summary>
    public bool Overwrite { get; init; }
}
