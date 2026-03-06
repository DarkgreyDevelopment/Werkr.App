namespace Werkr.Core.Security;

/// <summary>
/// Resolves and validates file-system paths against the configured allowlist.
/// Provides shared utilities for wildcard resolution and source/destination
/// validation used by all built-in action handlers.
/// </summary>
public interface IFilePathResolver {

    /// <summary>
    /// Resolves a single path to its full, normalized form and validates it
    /// against the allowlist. Throws <see cref="UnauthorizedAccessException"/>
    /// if the path is outside the configured allowed prefixes.
    /// </summary>
    /// <param name="path">The path to resolve and validate.</param>
    /// <returns>The fully resolved, normalized path.</returns>
    string ResolveSinglePath( string path );

    /// <summary>
    /// Resolves a wildcard source path (e.g. <c>C:\data\*.txt</c>), validates
    /// each matched file against the allowlist, and returns the matched file paths.
    /// Throws <see cref="UnauthorizedAccessException"/> if any resolved path
    /// falls outside the configured allowed prefixes.
    /// </summary>
    /// <param name="source">A file path that may contain wildcard characters.</param>
    /// <returns>An array of resolved, validated file paths.</returns>
    string[] ResolveFiles( string source );

    /// <summary>
    /// Validates that two paths are not the same (case-aware per platform) and
    /// resolves both to their full forms. Throws <see cref="ArgumentException"/>
    /// if source and destination refer to the same path.
    /// </summary>
    /// <param name="source">The source path.</param>
    /// <param name="destination">The destination path.</param>
    void ValidateSourceDestination(
        string source,
        string destination
    );
}
