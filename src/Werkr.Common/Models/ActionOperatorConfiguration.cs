namespace Werkr.Common.Models;

/// <summary>
/// Configuration options for the <c>ActionOperator</c> - the built-in action
/// handler dispatch engine. Bound from the <c>"ActionOperator"</c> configuration section.
/// </summary>
public sealed class ActionOperatorConfiguration {

    /// <summary>The configuration section name.</summary>
    public const string SectionName = "ActionOperator";

    /// <summary>
    /// Default timeout applied to each action handler invocation.
    /// Set to <see langword="null"/> to disable the timeout entirely.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; } = TimeSpan.FromHours( 1 );

    /// <summary>
    /// When <see langword="true"/>, network actions (HttpRequest, DownloadFile,
    /// TestConnection, SendWebhook, SendEmail, UploadFile) are permitted to execute.
    /// When <see langword="false"/> (default), all network actions fail with a descriptive error.
    /// </summary>
    public bool EnableNetworkActions { get; set; }

    /// <summary>
    /// URL prefix allowlist for network actions. When non-empty, only URLs matching
    /// at least one prefix are permitted. When empty, all URLs are allowed
    /// (subject to <see cref="AllowPrivateNetworks"/> and scheme restrictions).
    /// </summary>
    public string[] AllowedUrls { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/>, network actions may connect to private/reserved
    /// IP ranges (RFC 1918, loopback, link-local). When <see langword="false"/> (default),
    /// DNS-resolved addresses are validated and private IPs are rejected (SSRF protection).
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }
}
