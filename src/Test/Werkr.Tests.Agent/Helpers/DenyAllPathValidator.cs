using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that denies every path.
/// Used to test that handlers properly propagate allowlist rejections.
/// </summary>
internal sealed class DenyAllPathValidator : IPathAllowlistValidator {

    /// <summary>
    /// Always throws an <see cref="UnauthorizedAccessException"/> regardless of the supplied path.
    /// </summary>
    public void ValidatePath( string path ) =>
        throw new UnauthorizedAccessException( $"Path '{path}' is outside the configured allowlist." );

    /// <summary>
    /// Validates multiple paths. Throws an <see cref="UnauthorizedAccessException"/> on the first path.
    /// </summary>
    public void ValidatePaths( params string[] paths ) {
        foreach (string path in paths) {
            ValidatePath( path );
        }
    }

    /// <summary>
    /// Determines whether the given path is allowed. This implementation always returns <see langword="false"/>.
    /// </summary>
    public bool IsPathAllowed( string path ) => false;
}
