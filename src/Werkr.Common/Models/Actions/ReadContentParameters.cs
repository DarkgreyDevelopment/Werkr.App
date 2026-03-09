namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ReadContent action.</summary>
public sealed record ReadContentParameters {
    /// <summary>Full path of the file to read.</summary>
    public required string Path { get; init; }

    /// <summary>Text encoding (e.g. "utf-8", "ascii"). Default: "utf-8".</summary>
    public string Encoding { get; init; } = "utf-8";

    /// <summary>
    /// Maximum number of bytes to read. Null means read the entire file.
    /// </summary>
    public long? MaxBytes { get; init; }
}
