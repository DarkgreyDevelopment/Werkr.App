namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration DTO for agent-specific settings.
/// </summary>
public sealed class AgentSettings {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Agent";

    /// <summary>Display name for this agent instance.</summary>
    public string Name { get; set; } = "Default Agent";

    /// <summary>Port the agent gRPC service listens on.</summary>
    public int GrpcPort { get; set; } = 5100;

    /// <summary>Whether the PowerShell operator is enabled.</summary>
    public bool EnablePowerShell { get; set; } = true;

    /// <summary>Whether the system shell operator is enabled.</summary>
    public bool EnableSystemShell { get; set; } = true;

    /// <summary>PowerShell-specific settings.</summary>
    public PowerShellSettings PowerShell { get; set; } = new( );

    /// <summary>
    /// Maximum seconds to wait for active jobs to complete during graceful shutdown.
    /// Must be less than the host's shutdown timeout (e.g. systemd TimeoutStopSec).
    /// </summary>
    public int ShutdownTimeoutSeconds { get; set; } = 30;
}
