namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the DeleteFile action.</summary>
public sealed record DeleteFileParameters {
    /// <summary>Path of the file or directory to delete.</summary>
    public required string Path { get; init; }

    /// <summary>Whether to delete directories recursively.</summary>
    public bool Recursive { get; init; }

    /// <summary>Whether to remove read-only attributes before deletion.</summary>
    public bool Force { get; init; }
}
