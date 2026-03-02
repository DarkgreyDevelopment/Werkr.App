using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that only permits paths
/// under specified prefixes. Used for testing allowlist enforcement
/// in <see cref="Werkr.Agent.Security.FilePathResolver"/> tests.
/// </summary>
internal sealed class AllowPrefixValidator : IPathAllowlistValidator {

    private readonly string[] _allowedPrefixes;

    public AllowPrefixValidator( params string[] allowedPrefixes ) {
        _allowedPrefixes = allowedPrefixes;
    }

    public void ValidatePath( string path ) {
        if (!IsPathAllowed( path )) {
            throw new UnauthorizedAccessException(
                $"Path '{path}' is outside the configured allowlist." );
        }
    }

    public void ValidatePaths( params string[] paths ) {
        foreach (string path in paths) {
            ValidatePath( path );
        }
    }

    public bool IsPathAllowed( string path ) {
        string fullPath = Path.GetFullPath( path );
        foreach (string prefix in _allowedPrefixes) {
            string normalizedPrefix = Path.GetFullPath( prefix );
            if (fullPath.StartsWith( normalizedPrefix, StringComparison.OrdinalIgnoreCase )) {
                return true;
            }
        }
        return false;
    }
}
