namespace Werkr.Common.Models;

/// <summary>
/// Specifies the sort order for directory listing results.
/// </summary>
public enum DirectoryListSortBy {
    /// <summary>Sort by name (alphabetical).</summary>
    Name = 0,

    /// <summary>Sort by last modified date.</summary>
    Modified = 1,

    /// <summary>Sort by file size.</summary>
    Size = 2,

    /// <summary>No sorting - return entries in enumeration order.</summary>
    None = 3,
}
