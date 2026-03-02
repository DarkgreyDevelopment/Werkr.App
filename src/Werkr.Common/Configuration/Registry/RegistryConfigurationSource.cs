using Microsoft.Extensions.Configuration;

namespace Werkr.Common.Configuration.Registry;

/// <summary>
/// An <see cref="IConfigurationSource"/> that reads settings from the Windows
/// Registry under <c>HKLM\SOFTWARE\Werkr\{SubKey}</c>.
/// <para>
/// On non-Windows platforms this source is a no-op — it returns an empty provider.
/// </para>
/// </summary>
public sealed class RegistryConfigurationSource : IConfigurationSource {
    /// <summary>
    /// The registry sub-key under <c>HKLM\SOFTWARE\Werkr\</c>.
    /// For example <c>"Agent"</c> reads <c>HKLM\SOFTWARE\Werkr\Agent</c>.
    /// </summary>
    public string SubKey { get; set; } = string.Empty;

    /// <summary>
    /// Root path under HKLM. Default is <c>SOFTWARE\Werkr</c>.
    /// </summary>
    public string RootPath { get; set; } = @"SOFTWARE\Werkr";

    /// <inheritdoc />
    public IConfigurationProvider Build( IConfigurationBuilder builder ) {
        return new RegistryConfigurationProvider( this );
    }
}
