using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;

namespace Werkr.Common.Configuration.Registry;

/// <summary>
/// Reads configuration values from the Windows Registry.
/// <para>
/// Registry keys map to configuration paths using <c>:</c> as separator.
/// For example, <c>HKLM\SOFTWARE\Werkr\Agent</c> with value <c>Name = "MyAgent"</c>
/// becomes the configuration path <c>Agent:Name</c>.
/// </para>
/// <para>
/// Sub-keys are traversed recursively. On non-Windows platforms the provider
/// returns no data.
/// </para>
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="RegistryConfigurationProvider"/>.
/// </remarks>
/// <param name="source">The source configuration.</param>
public sealed class RegistryConfigurationProvider( RegistryConfigurationSource source ) : ConfigurationProvider {
    private readonly RegistryConfigurationSource _source = source ?? throw new ArgumentNullException( nameof( source ) );

    /// <inheritdoc />
    public override void Load( ) {
        Dictionary<string, string?> data = new( StringComparer.OrdinalIgnoreCase );

        if (RuntimeInformation.IsOSPlatform( OSPlatform.Windows )) {
            ReadRegistryWindows( data );
        }

        Data = data;
    }

    /// <summary>
    /// Opens the target registry key on Windows and recursively reads
    /// all values and sub-keys into the provided <paramref name="data"/> dictionary.
    /// </summary>
    [SupportedOSPlatform( "windows" )]
    private void ReadRegistryWindows( Dictionary<string, string?> data ) {
        string registryPath = string.IsNullOrEmpty( _source.SubKey )
            ? _source.RootPath
            : $@"{_source.RootPath}\{_source.SubKey}";

        using Microsoft.Win32.RegistryKey? key =
            Microsoft.Win32.Registry.LocalMachine.OpenSubKey( registryPath );

        if (key is null) {
            return;
        }

        ReadKeyRecursive(
            key,
            _source.SubKey,
            data
        );
    }

    /// <summary>
    /// Recursively reads all named values and child sub-keys from the
    /// given registry key, converting the registry hierarchy into
    /// colon-delimited configuration keys.
    /// </summary>
    [SupportedOSPlatform( "windows" )]
    private static void ReadKeyRecursive(
        Microsoft.Win32.RegistryKey key,
        string prefix,
        Dictionary<string, string?> data
    ) {
        // Read values at this level
        foreach (string valueName in key.GetValueNames( )) {
            string configKey = string.IsNullOrEmpty( prefix )
                ? valueName
                : $"{prefix}:{valueName}";

            object? value = key.GetValue( valueName );
            if (value is not null) {
                data[configKey] = value.ToString( );
            }
        }

        // Recurse into sub-keys
        foreach (string subKeyName in key.GetSubKeyNames( )) {
            using Microsoft.Win32.RegistryKey? subKey = key.OpenSubKey( subKeyName );
            if (subKey is not null) {
                string subPrefix = string.IsNullOrEmpty( prefix )
                    ? subKeyName
                    : $"{prefix}:{subKeyName}";
                ReadKeyRecursive(
                    subKey,
                    subPrefix,
                    data
                );
            }
        }
    }
}
