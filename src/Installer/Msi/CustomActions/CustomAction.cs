using System.Text.Json;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace Werkr.Installer.Msi.CustomActions;

/// <summary>
/// WiX custom actions for Werkr MSI installers.
/// Collects installer properties and writes appsettings.json on install.
/// </summary>
public class CustomActions {
    /// <summary>
    /// Converts installer properties into a JSON configuration string and stores
    /// it in the <c>CompletedAppSettingsJson</c> property for later use by
    /// <see cref="ConfigSaveExec"/>.
    /// </summary>
    [CustomAction]
    public static ActionResult ConvertPropertiesToCompletedAppSettingsJson( Session session ) {
        try {
            session.Log( "Begin ConvertPropertiesToCompletedAppSettingsJson" );

            string productName = GetSessionProperty( session, "ProductName" );
            string allowedHosts = GetSessionProperty( session, "ALLOWEDHOSTS" );

            // Build configuration based on product type
            Dictionary<string, object> config = new( ) {
                ["AllowedHosts"] = string.IsNullOrWhiteSpace( allowedHosts ) ? "*" : allowedHosts
            };

            if (productName.Contains( "Agent" )) {
                ConfigureAgentSettings( session, config );
            } else {
                ConfigureServerSettings( session, config );
            }

            ConfigureLogging( session, config );

            string jsonString = JsonSerializer.Serialize( config, new JsonSerializerOptions {
                WriteIndented = true
            } );

            session["CompletedAppSettingsJson"] = jsonString;
            session.Log( "End ConvertPropertiesToCompletedAppSettingsJson" );
        } catch (Exception e) {
            session.Log( $"An exception has occurred while converting properties to JSON. Error: {e.Message}" );
            return ActionResult.Failure;
        }
        return ActionResult.Success;
    }

    /// <summary>
    /// Writes the configuration JSON to <c>appsettings.json</c> in the install directory.
    /// This is a deferred custom action — it reads from <see cref="Session.CustomActionData"/>.
    /// </summary>
    [CustomAction]
    public static ActionResult ConfigSaveExec( Session session ) {
        try {
            session.Log( "Begin ConfigSaveExec" );

            string completedJson = GetSessionProperty( session, "CompletedAppSettingsJson", deferred: true );
            string installDir = GetSessionProperty( session, "INSTALLDIRECTORY", deferred: true );
            string appSettingsPath = Path.Combine( installDir, "appsettings.json" );

            if (File.Exists( appSettingsPath )) {
                File.Delete( appSettingsPath );
                session.Log( $"Deleted existing appsettings file: {appSettingsPath}" );
            }

            File.WriteAllText( appSettingsPath, completedJson );
            session.Log( $"Saved appsettings to: {appSettingsPath}" );

            // Also write install path to registry for service discovery
            WriteRegistrySettings( session, installDir );

            session.Log( "End ConfigSaveExec" );
        } catch (Exception e) {
            session.Log( $"An exception has occurred while saving configuration. Error: {e.Message}" );
            return ActionResult.Failure;
        }
        return ActionResult.Success;
    }

    #region Private Methods

    /// <summary>
    /// Retrieves a property from the installer session.
    /// </summary>
    private static string GetSessionProperty( Session session, string key, bool deferred = false ) {
        try {
            string result = deferred ? session.CustomActionData[key] : session[key];
            if (string.IsNullOrEmpty( result )) {
                session.Log( $"Install key '{key}' is null or empty." );
            }
            return result;
        } catch (KeyNotFoundException) {
            session.Log( $"Install key '{key}' not found." );
            return string.Empty;
        }
    }

    /// <summary>
    /// Configures agent-specific settings from installer properties.
    /// </summary>
    private static void ConfigureAgentSettings( Session session, Dictionary<string, object> config ) {
        string name = GetSessionProperty( session, "AGENTNAME" );
        string grpcPort = GetSessionProperty( session, "AGENTGRPCPORT" );
        string enablePwsh = GetSessionProperty( session, "ENABLEPWSH" );
        string enableShell = GetSessionProperty( session, "ENABLESHELL" );

        Dictionary<string, object> agentConfig = new( ) {
            ["Name"] = string.IsNullOrWhiteSpace( name ) ? "Default Agent" : name,
            ["GrpcPort"] = int.TryParse( grpcPort, out int port ) ? port : 5100,
            ["EnablePowerShell"] = bool.TryParse( enablePwsh, out bool pwsh ) && pwsh,
            ["EnableSystemShell"] = bool.TryParse( enableShell, out bool shell ) && shell
        };

        config["Agent"] = agentConfig;
    }

    /// <summary>
    /// Configures server-specific settings from installer properties.
    /// </summary>
    private static void ConfigureServerSettings( Session session, Dictionary<string, object> config ) {
        string name = GetSessionProperty( session, "SERVERNAME" );
        string allowRegistration = GetSessionProperty( session, "ALLOWREGISTRATION" );

        Dictionary<string, object> serverConfig = new( ) {
            ["Name"] = string.IsNullOrWhiteSpace( name ) ? "Werkr Server" : name,
            ["AllowRegistration"] = !bool.TryParse( allowRegistration, out bool allow ) || allow
        };

        config["Server"] = serverConfig;
    }

    /// <summary>
    /// Configures logging settings from installer properties.
    /// </summary>
    private static void ConfigureLogging( Session session, Dictionary<string, object> config ) {
        string defaultLevel = GetSessionProperty( session, "LOGLEVEL.DEFAULT" );
        string lifetimeLevel = GetSessionProperty( session, "LOGLEVEL.LIFETIME" );
        string aspNetLevel = GetSessionProperty( session, "LOGLEVEL.ASPNETCORE" );

        Dictionary<string, string> logLevel = new( ) {
            ["Default"] = string.IsNullOrWhiteSpace( defaultLevel ) ? "Warning" : defaultLevel,
            ["Microsoft.Hosting.Lifetime"] = string.IsNullOrWhiteSpace( lifetimeLevel ) ? "Information" : lifetimeLevel,
            ["Microsoft.AspNetCore"] = string.IsNullOrWhiteSpace( aspNetLevel ) ? "Warning" : aspNetLevel
        };

        config["Logging"] = new Dictionary<string, object> {
            ["LogLevel"] = logLevel
        };
    }

    /// <summary>
    /// Writes the install directory to the Windows Registry under
    /// <c>HKLM\SOFTWARE\Werkr\{ProductType}</c> so that the application
    /// can discover its configuration via the registry configuration provider.
    /// </summary>
    private static void WriteRegistrySettings( Session session, string installDir ) {
        try {
            string productName = GetSessionProperty( session, "ProductName", deferred: true );
            string subKey = productName.Contains( "Agent" ) ? "Agent" : "Server";
            string registryPath = $@"SOFTWARE\Werkr\{subKey}";

            using RegistryKey key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey( registryPath );
            key.SetValue( "InstallDirectory", installDir );
            key.SetValue( "ConfigPath", Path.Combine( installDir, "appsettings.json" ) );
            session.Log( $"Registry settings written to HKLM\\{registryPath}" );
        } catch (Exception e) {
            // Non-fatal — don't fail the install over registry writes
            session.Log( $"Warning: Could not write registry settings. Error: {e.Message}" );
        }
    }

    #endregion Private Methods
}
