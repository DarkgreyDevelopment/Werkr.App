namespace Werkr.Common.Models;

/// <summary>
/// Specifies the compression level for archive operations.
/// Maps to <see cref="System.IO.Compression.CompressionLevel"/> internally.
/// </summary>
public enum ArchiveCompressionLevel {
    /// <summary>Fastest compression speed at the cost of larger output.</summary>
    Fastest = 0,

    /// <summary>Balance between compression speed and output size.</summary>
    Optimal = 1,

    /// <summary>Smallest possible output at the cost of slower compression.</summary>
    SmallestSize = 2,
}
