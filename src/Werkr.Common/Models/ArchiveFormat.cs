namespace Werkr.Common.Models;

/// <summary>
/// Specifies the archive format for compress and expand operations.
/// </summary>
public enum ArchiveFormat {
    /// <summary>Standard Zip archive format.</summary>
    Zip = 0,

    /// <summary>GZip-compressed tar archive format.</summary>
    TarGz = 1,

    /// <summary>Auto-detect format from file extension (valid only for extraction).</summary>
    Auto = 2,
}
