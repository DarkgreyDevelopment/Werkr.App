namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration DTO for server-specific settings.
/// </summary>
public sealed class ServerSettings {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Server";

    /// <summary>Display name for this server instance.</summary>
    public string Name { get; set; } = "Werkr Server";

    /// <summary>Whether new agent registration is allowed.</summary>
    public bool AllowRegistration { get; set; } = true;
}
