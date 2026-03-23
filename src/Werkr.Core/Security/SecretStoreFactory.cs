using System.Runtime.InteropServices;

namespace Werkr.Core.Security;

/// <summary>
/// Factory that creates the platform-appropriate <see cref="ISecretStore"/>
/// implementation based on the current operating system.
/// </summary>
public static class SecretStoreFactory {
    /// <summary>
    /// Creates the appropriate <see cref="ISecretStore"/> for the current platform.
    /// </summary>
    /// <returns>A platform-specific <see cref="ISecretStore"/> instance.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the current operating system is not supported.
    /// </exception>
    public static ISecretStore Create( ) {
        return RuntimeInformation.IsOSPlatform( OSPlatform.Windows )
            ? new WindowsSecretStore( )
            : RuntimeInformation.IsOSPlatform( OSPlatform.OSX )
            ? new MacOsSecretStore( )
            : RuntimeInformation.IsOSPlatform( OSPlatform.Linux )
            ? (ISecretStore)new LinuxSecretStore( )
            : throw new PlatformNotSupportedException(
                "Werkr secret store is not supported on the current operating system." );
    }
}
