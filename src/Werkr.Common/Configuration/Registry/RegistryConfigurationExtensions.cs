using Microsoft.Extensions.Configuration;

namespace Werkr.Common.Configuration.Registry;

/// <summary>
/// Extension methods for adding <see cref="RegistryConfigurationSource"/>
/// to an <see cref="IConfigurationBuilder"/>.
/// </summary>
public static class RegistryConfigurationExtensions {
    /// <summary>
    /// Adds the Windows Registry as a configuration source.
    /// Reads from <c>HKLM\SOFTWARE\Werkr\{subKey}</c>.
    /// On non-Windows platforms this is a safe no-op.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="subKey">
    /// The sub-key under <c>SOFTWARE\Werkr</c> to read.
    /// For example <c>"Agent"</c> or <c>"Server"</c>.
    /// Pass <see langword="null"/> or empty to read the root key.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static IConfigurationBuilder AddWerkrRegistry(
        this IConfigurationBuilder builder,
        string subKey = "" ) {
        return builder.Add( new RegistryConfigurationSource { SubKey = subKey } );
    }
}
