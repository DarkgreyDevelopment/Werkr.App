using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that only permits paths
/// under specified prefixes. Used for testing allowlist enforcement
/// in <see cref="Werkr.Agent.Security.FilePathResolver"/> tests.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="AllowPrefixValidator"/>
/// class with the specified set of allowed directory prefixes.
/// </remarks>
internal sealed class AllowPrefixValidator( params string[] allowedPrefixes ) : IPathAllowlistValidator {

    /// <summary>
    /// The set of directory prefixes that are considered allowed.
    /// </summary>
    private readonly string[] _allowedPrefixes = allowedPrefixes;

    /// <summary>
    /// Validates the specified path against the configured prefixes.
    /// Throws an <see cref="UnauthorizedAccessException"/> when the path
    /// is outside every allowed prefix.
    /// </summary>
    public void ValidatePath( string path ) {
        if (!IsPathAllowed( path )) {
            throw new UnauthorizedAccessException(
                $"Path '{path}' is outside the configured allowlist." );
        }
    }

    /// <summary>
    /// Validates each of the specified paths against the configured
    /// prefixes. Throws an <see cref="UnauthorizedAccessException"/> on the
    /// first path that is not allowed.
    /// </summary>
    public void ValidatePaths( params string[] paths ) {
        foreach (string path in paths) {
            ValidatePath( path );
        }
    }

    /// <summary>
    /// Determines whether the given path is allowed by checking if its
    /// fully-qualified form starts with any of the configured prefixes
    /// (case-insensitive comparison).
    /// </summary>
    public bool IsPathAllowed( string path ) {
        string fullPath = Path.GetFullPath( path );
        foreach (string prefix in _allowedPrefixes) {
            string normalizedPrefix = Path.GetFullPath( prefix );
            if (fullPath.StartsWith(
                normalizedPrefix,
                StringComparison.OrdinalIgnoreCase
            )) {
                return true;
            }
        }
        return false;
    }
}
