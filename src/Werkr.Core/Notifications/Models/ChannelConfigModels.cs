namespace Werkr.Core.Notifications.Models;

/// <summary>
/// Channel configuration for email delivery.
/// Stored encrypted in <c>NotificationChannel.Configuration</c>.
/// </summary>
public sealed class EmailChannelConfig {
    /// <summary>Credential name for SMTP server settings (host, port, TLS, username, password).</summary>
    public string SmtpCredentialName { get; set; } = string.Empty;

    /// <summary>Sender email address (the "from" address).</summary>
    public string SenderAddress { get; set; } = string.Empty;

    /// <summary>Sender display name.</summary>
    public string SenderDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Channel configuration for webhook delivery.
/// Stored encrypted in <c>NotificationChannel.Configuration</c>.
/// </summary>
public sealed class WebhookChannelConfig {
    /// <summary>Target URL for HTTP POST (validated — non-HTTPS rejected in production).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Authentication type: "header", "hmac", or null (no auth).</summary>
    public string? AuthType { get; set; }

    /// <summary>Custom header name for header-based auth (default: X-Werkr-Secret).</summary>
    public string? AuthHeaderName { get; set; }

    /// <summary>Credential name for header secret value.</summary>
    public string? AuthCredentialName { get; set; }

    /// <summary>Credential name for HMAC shared secret.</summary>
    public string? HmacCredentialName { get; set; }

    /// <summary>Number of retry attempts on failure (default: 3).</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Exponential backoff base in seconds (default: 30).</summary>
    public int RetryBackoffBaseSeconds { get; set; } = 30;
}

/// <summary>
/// Channel configuration for in-app delivery.
/// In-app channels have no configuration beyond enabled/disabled.
/// </summary>
public sealed class InAppChannelConfig {
    // No configuration needed — in-app channels are always delivery-ready.
}
