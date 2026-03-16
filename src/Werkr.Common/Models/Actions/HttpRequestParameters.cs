namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>HttpRequest</c> action — sends an HTTP request and captures
/// the response status, headers, and body. Supports all HTTP methods, custom headers,
/// request body (from parameter or workflow variable), and optional output file.
/// </summary>
public sealed record HttpRequestParameters {

    /// <summary>The target URL (absolute, http/https only).</summary>
    public required string Url { get; init; }

    /// <summary>HTTP method. Default: <c>GET</c>.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Optional request headers (name → value).</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Optional request body. When set, takes precedence over <c>inputVariableValue</c>.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Content-Type header for the request body.
    /// Defaults to <c>application/json</c> when body comes from a workflow variable.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>Per-request timeout in seconds. Default: 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Accepted HTTP status codes. If the response code is not in this list,
    /// the action fails. Default: <c>[200]</c>.
    /// </summary>
    public int[] ExpectedStatusCodes { get; init; } = [200];

    /// <summary>
    /// Optional file path to write the full response body to disk.
    /// When set, the response body is streamed to file instead of held in memory.
    /// </summary>
    public string? OutputFilePath { get; init; }

    /// <summary>
    /// When <see langword="true"/>, HTTP redirects (3xx) are followed automatically.
    /// Default: <see langword="false"/> (prevents auth-token forwarding to redirect targets).
    /// </summary>
    public bool FollowRedirects { get; init; }
}
