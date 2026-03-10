using Microsoft.Extensions.Configuration;
using Werkr.Common.Configuration.Registry;

namespace Werkr.Common.Extensions;

/// <summary>
/// Extension methods for <see cref="IConfigurationBuilder"/>.
/// </summary>
public static class ConfigurationBuilderExtensions {
    /// <summary>
    /// Adds platform-specific configuration sources for Werkr:
    /// <list type="bullet">
    ///   <item>
    ///     <description><b>Windows:</b> reads from <c>HKLM\SOFTWARE\Werkr\{subKey}</c> via
    ///     <see cref="RegistryConfigurationExtensions.AddWerkrRegistry"/>.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Linux / Docker:</b> reads a JSON file specified by the
    ///     <c>WERKR_CONFIG_PATH</c> environment variable.</description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="registrySubKey">
    /// The registry sub-key under <c>SOFTWARE\Werkr</c> to read on Windows.
    /// For example <c>"Agent"</c> or <c>"Server"</c>. Ignored on non-Windows platforms.
    /// </param>
    /// <returns>The configuration builder for chaining.</returns>
    public static IConfigurationBuilder AddWerkrConfigPath(
        this IConfigurationBuilder builder,
        string registrySubKey = ""
    ) {
        // Windows: read from HKLM\SOFTWARE\Werkr\{subKey}
        _ = builder.AddWerkrRegistry( registrySubKey );

        // Linux / Docker: read from WERKR_CONFIG_PATH env var
        string? configPath = Environment.GetEnvironmentVariable( "WERKR_CONFIG_PATH" );
        if (!string.IsNullOrEmpty( configPath )) {
            _ = builder.AddJsonFile(
                configPath,
                optional: true,
                reloadOnChange: true
            );
        }
        return builder;
    }
}
