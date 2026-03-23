namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>SendWebhook</c> action. Sends a JSON POST to the specified
/// URL with an optional payload from the parameter or the input variable.
/// </summary>
public sealed record SendWebhookParameters {

    /// <summary>The webhook endpoint URL.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// The JSON payload to send. If omitted the input variable value is used as the body.
    /// </summary>
    public string? Payload { get; init; }

    /// <summary>Optional additional request headers.</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Request timeout in seconds. Defaults to 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
