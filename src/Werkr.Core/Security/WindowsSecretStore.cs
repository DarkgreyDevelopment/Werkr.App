using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Werkr.Core.Security;

/// <summary>
/// Windows implementation of <see cref="ISecretStore"/> using DPAPI
/// (<see cref="ProtectedData"/>) with <see cref="DataProtectionScope.CurrentUser"/>.
/// Encrypted blobs are stored to <c>%LOCALAPPDATA%\Werkr\secrets\{key}.bin</c>.
/// </summary>
[SupportedOSPlatform( "windows" )]
public class WindowsSecretStore : ISecretStore {
    private readonly string _basePath;

    /// <summary>Creates a new <see cref="WindowsSecretStore"/>.</summary>
    public WindowsSecretStore( ) {
        string localAppData = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
        _basePath = Path.Combine(
            localAppData,
            "Werkr",
            "secrets"
        );
        _ = Directory.CreateDirectory( _basePath );
    }

    /// <inheritdoc/>
    public Task<string?> GetSecretAsync( string key ) {
        string filePath = GetFilePath( key );
        if (!File.Exists( filePath )) {
            return Task.FromResult<string?>( null );
        }

        byte[] encryptedBytes = File.ReadAllBytes( filePath );
        byte[] decryptedBytes = ProtectedData.Unprotect(
            encryptedBytes,
            null,
            DataProtectionScope.CurrentUser
        );
        string value = System.Text.Encoding.UTF8.GetString( decryptedBytes );
        return Task.FromResult<string?>( value );
    }

    /// <inheritdoc/>
    public Task SetSecretAsync(
        string key,
        string value
    ) {
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes( value );
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes,
            null,
            DataProtectionScope.CurrentUser
        );
        string filePath = GetFilePath( key );
        File.WriteAllBytes(
            filePath,
            encryptedBytes
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteSecretAsync( string key ) {
        string filePath = GetFilePath( key );
        if (File.Exists( filePath )) {
            File.Delete( filePath );
        }

        return Task.CompletedTask;
    }

    private string GetFilePath( string key ) {
        // Sanitize key for file system
        string safeKey = string.Join(
            "_",
            key.Split( Path.GetInvalidFileNameChars( ) )
        );
        return Path.Combine(
            _basePath,
            safeKey + ".bin"
        );
    }
}
