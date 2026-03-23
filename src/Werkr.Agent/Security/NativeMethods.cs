using System.Runtime.InteropServices;

namespace Werkr.Agent.Security;

/// <summary>
/// Platform-specific native methods for file path resolution.
/// Contains the <see cref="GetLongPathNameW"/> P/Invoke for expanding 8.3 short file
/// names on Windows. On non-Windows platforms, all methods are safe no-ops that
/// return the input path unchanged.
/// </summary>
internal static partial class NativeMethods {

    /// <summary>
    /// Expands 8.3 short file name segments (e.g. <c>PROGRA~1</c>) to their
    /// long-name equivalents (e.g. <c>Program Files</c>) on Windows.
    /// Returns the original <paramref name="path"/> unchanged when:
    /// <list type="bullet">
    ///   <item>Running on a non-Windows platform (8.3 names do not exist).</item>
    ///   <item>The Win32 call fails (e.g. path does not exist on disk).</item>
    ///   <item>No 8.3 segments are present (expanded path equals input).</item>
    /// </list>
    /// </summary>
    /// <param name="path">The file-system path to expand.</param>
    /// <returns>The expanded long path, or the original path if expansion is unnecessary or unavailable.</returns>
    internal static string GetLongPath( string path ) {
        return !RuntimeInformation.IsOSPlatform( OSPlatform.Windows ) ? path : GetLongPathWindows( path );
    }

    /// <summary>
    /// Windows-specific implementation using the two-call pattern:
    /// first call retrieves the required buffer size, second call
    /// performs the actual expansion.
    /// </summary>
    /// <param name="path">The file-system path to expand.</param>
    /// <returns>The expanded long path, or the original path on failure.</returns>
    private static string GetLongPathWindows( string path ) {
        // First call: determine required buffer size.
        // Pass buffer length 0 to get the required size (including null terminator).
        uint requiredSize = GetLongPathNameW( path, null, 0 );

        if (requiredSize == 0) {
            // GetLongPathNameW returns 0 on failure (e.g. path does not exist).
            // Fall back to the original path — the caller will proceed with
            // whatever normalization Path.GetFullPath already applied.
            return path;
        }

        // Second call: allocate exact buffer and retrieve the long path.
        char[] buffer = new char[requiredSize];
        uint written = GetLongPathNameW( path, buffer, requiredSize );

        if (written == 0 || written > requiredSize) {
            // Unexpected failure or buffer overflow — fall back gracefully.
            return path;
        }

        return new string( buffer, 0, (int)written );
    }

    /// <summary>
    /// Retrieves the long path form of the specified path.
    /// </summary>
    /// <param name="lpszShortPath">The path to expand.</param>
    /// <param name="lpszLongPath">
    /// A buffer that receives the long path form. May be <see langword="null"/> when
    /// <paramref name="cchBuffer"/> is 0 (to query the required size).
    /// </param>
    /// <param name="cchBuffer">
    /// The size of the <paramref name="lpszLongPath"/> buffer, in characters.
    /// </param>
    /// <returns>
    /// If the call succeeds and the buffer is large enough, the number of
    /// characters copied (excluding the null terminator). If the buffer is too
    /// small, the required size (including the null terminator). Returns 0 on failure.
    /// </returns>
    [LibraryImport( "kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true )]
    private static partial uint GetLongPathNameW(
        string lpszShortPath,
        [Out] char[]? lpszLongPath,
        uint cchBuffer );
}
