namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ListDirectory action.</summary>
public sealed record ListDirectoryParameters {
    /// <summary>Full path of the directory to enumerate.</summary>
    public required string Path { get; init; }

    /// <summary>Glob pattern to filter entries. Default: "*".</summary>
    public string Pattern { get; init; } = "*";

    /// <summary>Whether to search subdirectories recursively.</summary>
    public bool Recursive { get; init; }

    /// <summary>What type of entries to return: File, Directory, or Any.</summary>
    public PathType Type { get; init; } = PathType.File;

    /// <summary>Sort order for the returned entries.</summary>
    public DirectoryListSortBy SortBy { get; init; } = DirectoryListSortBy.Name;
}
