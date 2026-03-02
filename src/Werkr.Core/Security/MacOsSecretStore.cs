using System.Diagnostics;
using System.Runtime.Versioning;

namespace Werkr.Core.Security;

/// <summary>
/// macOS implementation of <see cref="ISecretStore"/> using the Keychain
/// via the <c>security</c> command-line tool.
/// </summary>
[SupportedOSPlatform( "osx" )]
public class MacOsSecretStore : ISecretStore {
    private const string ServiceName = "Werkr";

    /// <inheritdoc/>
    public async Task<string?> GetSecretAsync( string key ) {
        (int exitCode, string stdout, _) = await RunSecurityAsync(
            "find-generic-password",
            $"-s \"{ServiceName}\" -a \"{key}\" -w"
        ).ConfigureAwait( false );

        return exitCode != 0 ? null : stdout.Trim( );
    }

    /// <inheritdoc/>
    public async Task SetSecretAsync( string key, string value ) {
        // Delete existing entry first (ignore errors if it doesn't exist)
        _ = await RunSecurityAsync(
            "delete-generic-password",
            $"-s \"{ServiceName}\" -a \"{key}\""
        ).ConfigureAwait( false );

        (int exitCode, _, string stderr) = await RunSecurityAsync(
            "add-generic-password",
            $"-s \"{ServiceName}\" -a \"{key}\" -w \"{value}\" -U"
        ).ConfigureAwait( false );

        if (exitCode != 0) {
            throw new InvalidOperationException( $"Failed to store secret in Keychain: {stderr}" );
        }
    }

    /// <inheritdoc/>
    public async Task DeleteSecretAsync( string key ) {
        _ = await RunSecurityAsync(
            "delete-generic-password",
            $"-s \"{ServiceName}\" -a \"{key}\""
        ).ConfigureAwait( false );
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunSecurityAsync(
        string command, string arguments ) {
        ProcessStartInfo psi = new( ) {
            FileName = "security",
            Arguments = $"{command} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new( ) { StartInfo = psi };
        _ = process.Start( );

        string stdout = await process.StandardOutput.ReadToEndAsync( ).ConfigureAwait( false );
        string stderr = await process.StandardError.ReadToEndAsync( ).ConfigureAwait( false );
        await process.WaitForExitAsync( ).ConfigureAwait( false );

        return (process.ExitCode, stdout, stderr);
    }
}
