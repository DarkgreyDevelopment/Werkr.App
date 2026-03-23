namespace Werkr.Common.Models;

/// <summary>
/// Discriminator for the <c>TestExists</c> action to specify
/// whether to assert file existence, directory existence, or either.
/// </summary>
public enum PathType {
    /// <summary>Assert only file existence.</summary>
    File = 0,

    /// <summary>Assert only directory existence.</summary>
    Directory = 1,

    /// <summary>Assert either file or directory exists (default).</summary>
    Any = 2,
}
