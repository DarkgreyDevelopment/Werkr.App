using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using Werkr.Common.Models;
using Werkr.Core.Security;

namespace Werkr.Agent.Security;

/// <summary>
/// Validates that file paths are within the configured allowlist.
/// When enforcement is disabled (the default), all paths are permitted.
/// When enforcement is enabled, paths outside all allowed prefixes are rejected.
/// </summary>
/// <remarks>
/// Uses <see cref="IOptionsMonitor{T}"/> for hot-reload support - if the
/// configuration changes at runtime, the validator picks up the new values
/// on the next call without requiring a restart.
/// </remarks>
/// <remarks>Creates a new <see cref="PathAllowlistValidator"/>.</remarks>
public sealed partial class PathAllowlistValidator(
    IOptionsMonitor<AllowedPathsConfiguration> options,
    ILogger<PathAllowlistValidator> logger
    ) : IPathAllowlistValidator {

    private readonly IOptionsMonitor<AllowedPathsConfiguration> _options = options;
    private readonly ILogger<PathAllowlistValidator> _logger = logger;
    private readonly StringComparison _comparison = RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <inheritdoc/>
    public void ValidatePath( string path ) {
        if (!IsPathAllowed( path )) {
            AllowedPathsConfiguration config = _options.CurrentValue;
            string allowlist = string.Join( ", ", config.Paths );
            _logger.LogWarning(
                "Path '{Path}' is outside the configured allowlist [{Allowlist}]", path, allowlist );
            throw new UnauthorizedAccessException(
                $"Path '{path}' is outside the configured allowlist. " +
                $"Allowed prefixes: [{allowlist}]" );
        }
    }

    /// <inheritdoc/>
    public void ValidatePaths( params string[] paths ) {
        foreach (string path in paths) {
            ValidatePath( path );
        }
    }

    /// <inheritdoc/>
    public bool IsPathAllowed( string path ) {
        AllowedPathsConfiguration config = _options.CurrentValue;

        if (!config.EnforceAllowlist) {
            return true;
        }

        if (config.Paths.Count == 0) {
            // Enforcement enabled but no paths configured — deny all
            _logger.LogWarning( "Allowlist enforcement is enabled but no paths are configured. Denying all paths." );
            return false;
        }

        string normalizedPath = NormalizePath( path );

        // Reject dangerous path patterns
        if (IsDangerousPath( normalizedPath )) {
            _logger.LogWarning( "Path '{Path}' uses a dangerous pattern and was rejected.", path );
            return false;
        }

        foreach (string allowedPrefix in config.Paths) {
            string normalizedPrefix = NormalizePath( allowedPrefix );
            if (normalizedPath.StartsWith( normalizedPrefix, _comparison )) {
                return true;
            }
        }

        return false;
    }

    private string NormalizePath( string path ) {
        // Resolve to full path and normalize separators
        string fullPath = Path.GetFullPath( path );

        // Expand 8.3 short names (e.g. PROGRA~1 → Program Files) on Windows.
        // No-op on non-Windows platforms.
        fullPath = NativeMethods.GetLongPath( fullPath );

        // Resolve symlinks/junctions to their final target so that the allowlist
        // comparison uses the real path, not the link path.
        try {
            FileSystemInfo? resolved = File.ResolveLinkTarget( fullPath, returnFinalTarget: true );
            if (resolved is not null) {
                fullPath = resolved.FullName;
            }
        } catch (IOException ex) {
            // Target may not exist yet (create-before-write scenarios).
            // Fall back to the best-available resolved path.
            if (_logger.IsEnabled( LogLevel.Debug )) {
                _logger.LogDebug( ex, "Could not resolve symlink target for '{Path}'. Using normalized path.", fullPath );
            }
        }

        // Normalize directory separators to the platform-native separator
        fullPath = fullPath.Replace( Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar );

        return fullPath;
    }

    private static bool IsDangerousPath( string normalizedPath ) {
        if (RuntimeInformation.IsOSPlatform( OSPlatform.Windows )) {
            // Reject \\?\ prefix (extended-length path)
            if (normalizedPath.StartsWith( @"\\?\", StringComparison.Ordinal )) {
                return true;
            }

            // Reject \\.\ device paths
            if (normalizedPath.StartsWith( @"\\.\", StringComparison.Ordinal )) {
                return true;
            }

            // Reject UNC paths (\\server\share)
            if (normalizedPath.StartsWith( @"\\", StringComparison.Ordinal )) {
                return true;
            }

            // Reject Alternate Data Streams (colon in non-drive position)
            // Drive letter colon is at index 1 (e.g., "C:\")
            int firstColon = normalizedPath.IndexOf( ':', StringComparison.Ordinal );
            if (firstColon > 1 || normalizedPath.IndexOf( ':', firstColon + 1 ) >= 0) {
                return true;
            }
        }

        // Reject paths that still contain .. traversal after normalization
        // (Path.GetFullPath should have resolved these, but double-check)
        string[] segments = normalizedPath.Split( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
        foreach (string segment in segments) {
            if (segment == "..") {
                return true;
            }
        }

        return false;
    }
}
