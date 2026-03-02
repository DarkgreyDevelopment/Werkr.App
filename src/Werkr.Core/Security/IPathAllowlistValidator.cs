namespace Werkr.Core.Security;

/// <summary>
/// Validates that file paths are within the configured allowlist.
/// When enforcement is disabled (the default), all paths are permitted.
/// When enforcement is enabled, paths outside all allowed prefixes are rejected.
/// </summary>
/// <remarks>
/// Path allowlist enforcement occurs exclusively on the Agent.
/// The API layer cannot validate paths against allowlists because the
/// target agent is not known at task-creation time (tasks are dispatched
/// by tag matching). The Agent is the sole enforcement point.
/// </remarks>
public interface IPathAllowlistValidator {
    /// <summary>
    /// Validates that <paramref name="path"/> is within the configured allowlist.
    /// Throws <see cref="UnauthorizedAccessException"/> with a descriptive message
    /// if the path is outside all allowed prefixes and enforcement is enabled.
    /// </summary>
    /// <param name="path">The filesystem path to validate.</param>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the path is outside the allowlist and enforcement is enabled.
    /// </exception>
    void ValidatePath( string path );

    /// <summary>
    /// Validates that all specified paths are within the configured allowlist.
    /// Useful for operations involving source and destination (e.g. copy, move).
    /// </summary>
    /// <param name="paths">The filesystem paths to validate.</param>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when any path is outside the allowlist and enforcement is enabled.
    /// </exception>
    void ValidatePaths( params string[] paths );

    /// <summary>
    /// Non-throwing check for whether <paramref name="path"/> is within the configured allowlist.
    /// Returns <c>true</c> when enforcement is disabled or the path is within an allowed prefix.
    /// </summary>
    /// <param name="path">The filesystem path to check.</param>
    /// <returns><c>true</c> if the path is permitted; <c>false</c> otherwise.</returns>
    bool IsPathAllowed( string path );
}
