using System.Runtime.InteropServices;
using Werkr.Core.Security;

namespace Werkr.Agent.Security;

/// <summary>
/// Resolves and validates file-system paths against the configured allowlist.
/// Provides shared wildcard resolution and source/destination validation used
/// by all built-in action handlers. Delegates path validation to
/// <see cref="IPathAllowlistValidator"/>.
/// </summary>
/// <remarks>Creates a new <see cref="FilePathResolver"/>.</remarks>
public sealed class FilePathResolver( IPathAllowlistValidator pathValidator ) : IFilePathResolver {

    private readonly IPathAllowlistValidator _pathValidator = pathValidator;
    private readonly StringComparison _comparison = RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <inheritdoc/>
    public string ResolveSinglePath( string path ) {
        string fullPath = Path.GetFullPath( path );
        _pathValidator.ValidatePath( fullPath );
        return fullPath;
    }

    /// <inheritdoc/>
    public string[] ResolveFiles( string source ) {
        FileInfo fileInfo = new( source );
        string dirName = fileInfo.DirectoryName
            ?? throw new InvalidOperationException( "Source must be rooted under a directory." );

        if (!Directory.Exists( dirName )) {
            return [];
        }

        string[] files = Directory.GetFiles( dirName, fileInfo.Name );

        // Validate each resolved file individually against the allowlist.
        // This closes the glob-security gap where a wildcard inside an allowed
        // directory could match symlinks pointing outside the allowlist.
        foreach (string file in files) {
            _pathValidator.ValidatePath( file );
        }

        return files;
    }

    /// <inheritdoc/>
    public void ValidateSourceDestination( string source, string destination ) {
        string fullSource = Path.GetFullPath( source );
        string fullDestination = Path.GetFullPath( destination );

        if (fullSource.Equals( fullDestination, _comparison )) {
            throw new ArgumentException( "Source path cannot be the same as the Destination path." );
        }
    }
}
