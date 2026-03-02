using Werkr.Core.Security;

namespace Werkr.Tests.Agent.Helpers;

/// <summary>
/// Fake <see cref="IPathAllowlistValidator"/> that denies every path.
/// Used to test that handlers properly propagate allowlist rejections.
/// </summary>
internal sealed class DenyAllPathValidator : IPathAllowlistValidator {

    public void ValidatePath( string path ) =>
        throw new UnauthorizedAccessException( $"Path '{path}' is outside the configured allowlist." );

    public void ValidatePaths( params string[] paths ) {
        foreach (string path in paths) {
            ValidatePath( path );
        }
    }

    public bool IsPathAllowed( string path ) => false;
}
