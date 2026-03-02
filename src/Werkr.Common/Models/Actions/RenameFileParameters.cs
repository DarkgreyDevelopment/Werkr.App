namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the RenameFile action.</summary>
public sealed record RenameFileParameters {
    /// <summary>Full path of the file or directory to rename.</summary>
    public required string Path { get; init; }

    /// <summary>New name (not a full path — just the file or directory name).</summary>
    public required string NewName { get; init; }

    /// <summary>Whether to overwrite an existing item with the same name.</summary>
    public bool Overwrite { get; init; }
}
