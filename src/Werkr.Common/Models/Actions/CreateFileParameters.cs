namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the CreateFile action.</summary>
public sealed record CreateFileParameters {
    /// <summary>Full path of the file to create.</summary>
    public required string Path { get; init; }

    /// <summary>Optional content to write into the new file.</summary>
    public string? Content { get; init; }

    /// <summary>Whether to overwrite the file if it already exists.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Text encoding for the file content (e.g. "utf-8", "ascii").</summary>
    public string Encoding { get; init; } = "utf-8";

    /// <summary>Whether to create parent directories if they do not exist.</summary>
    public bool CreateParentDirectories { get; init; } = true;
}
