using System.Reflection;

namespace Werkr.Agent;

/// <summary>
/// Provides the agent's assembly version string for use during registration and heartbeat reporting.
/// </summary>
internal static class VersionHelper {
    private static readonly string s_version = Assembly.GetEntryAssembly( )
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>( )
        ?.InformationalVersion ?? "unknown";

    /// <summary>Gets the agent's informational version string.</summary>
    public static string GetAgentVersion( ) => s_version;
}
