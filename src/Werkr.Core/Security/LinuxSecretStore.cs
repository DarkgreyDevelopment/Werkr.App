using System.Diagnostics;
using System.Runtime.Versioning;

namespace Werkr.Core.Security;

/// <summary>
/// Linux implementation of <see cref="ISecretStore"/> using libsecret
/// via the <c>secret-tool</c> command-line utility. Falls back to
/// file-based storage in <c>~/.config/werkr/secrets/</c> if
/// <c>secret-tool</c> is not available.
/// </summary>
[SupportedOSPlatform( "linux" )]
public class LinuxSecretStore : ISecretStore {
    private const string SchemaAttribute = "werkr-key";
    private readonly bool _useSecretTool;
    private readonly string _fallbackPath;

    /// <summary>Creates a new <see cref="LinuxSecretStore"/>.</summary>
    public LinuxSecretStore( ) {
        _useSecretTool = IsSecretToolAvailable( );

        string configDir = Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData );
        if (string.IsNullOrEmpty( configDir )) {
            configDir = Path.Combine(
                Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ),
                ".config"
            );
        }

        _fallbackPath = Path.Combine(
            configDir,
            "werkr",
            "secrets"
        );
        if (!_useSecretTool) {
            _ = Directory.CreateDirectory( _fallbackPath );
            // Restrict permissions: owner-only
            File.SetUnixFileMode( _fallbackPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetSecretAsync( string key ) {
        if (_useSecretTool) {
            (
                int exitCode,
                string stdout,
                _
            ) = await RunSecretToolAsync(
                "lookup", $"{SchemaAttribute} {key}"
            ).ConfigureAwait( false );

            return exitCode == 0 ? stdout.Trim( ) : null;
        }

        string filePath = GetFallbackFilePath( key );
        return !File.Exists( filePath ) ? null : await File.ReadAllTextAsync( filePath ).ConfigureAwait( false );
    }

    /// <inheritdoc/>
    public async Task SetSecretAsync(
        string key,
        string value
    ) {
        if (_useSecretTool) {
            (
                int exitCode,
                _,
                string stderr
            ) = await RunSecretToolAsync(
                "store", $"--label=\"Werkr: {key}\" {SchemaAttribute} {key}",
                stdinData: value
            ).ConfigureAwait( false );

            if (exitCode != 0) {
                throw new InvalidOperationException( $"Failed to store secret via secret-tool: {stderr}" );
            }

            return;
        }

        string filePath = GetFallbackFilePath( key );
        await File.WriteAllTextAsync(
            filePath,
            value
        ).ConfigureAwait( false );
        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite
        );
    }

    /// <inheritdoc/>
    public async Task DeleteSecretAsync( string key ) {
        if (_useSecretTool) {
            _ = await RunSecretToolAsync(
                "clear", $"{SchemaAttribute} {key}"
            ).ConfigureAwait( false );
            return;
        }

        string filePath = GetFallbackFilePath( key );
        if (File.Exists( filePath )) {
            File.Delete( filePath );
        }
    }

    private string GetFallbackFilePath( string key ) {
        string safeKey = string.Join(
            "_",
            key.Split( Path.GetInvalidFileNameChars( ) )
        );
        return Path.Combine(
            _fallbackPath,
            safeKey
        );
    }

    private static bool IsSecretToolAvailable( ) {
        try {
            ProcessStartInfo psi = new( ) {
                FileName = "which",
                Arguments = "secret-tool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new( ) { StartInfo = psi };
            _ = process.Start( );
            _ = process.WaitForExit( 3000 );
            return process.ExitCode == 0;
        } catch {
            return false;
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunSecretToolAsync(
        string command, string arguments, string? stdinData = null ) {
        ProcessStartInfo psi = new( ) {
            FileName = "secret-tool",
            Arguments = $"{command} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new( ) { StartInfo = psi };
        _ = process.Start( );

        if (stdinData is not null) {
            await process.StandardInput.WriteAsync( stdinData ).ConfigureAwait( false );
            process.StandardInput.Close( );
        }

        string stdout = await process.StandardOutput.ReadToEndAsync( ).ConfigureAwait( false );
        string stderr = await process.StandardError.ReadToEndAsync( ).ConfigureAwait( false );
        await process.WaitForExitAsync( ).ConfigureAwait( false );

        return (process.ExitCode, stdout, stderr);
    }
}
