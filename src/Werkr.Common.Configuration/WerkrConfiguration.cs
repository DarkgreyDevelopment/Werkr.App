namespace Werkr.Common.Configuration;

/// <summary>
/// Central configuration settings for the Werkr application.
/// Compatible with the options pattern (<c>IOptions&lt;T&gt;</c>).
/// </summary>
public sealed class WerkrConfiguration {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Werkr";

    /// <summary>Connection string for the primary database.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Default RSA key size in bits.</summary>
    public int DefaultKeySize { get; set; } = 4096;

    /// <summary>The Server's gRPC endpoint URL (embedded in registration bundles).</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>The Agent's gRPC endpoint URL (sent during registration).</summary>
    public string AgentUrl { get; set; } = string.Empty;

    /// <summary>Default timeout for command execution in minutes.</summary>
    public int DefaultCommandTimeoutMinutes { get; set; } = 30;
}
