namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>DownloadFile</c> action — downloads a file from a URL
/// to a local destination path with optional overwrite control.
/// </summary>
public sealed record DownloadFileParameters {

    /// <summary>The URL to download from (absolute, http/https only).</summary>
    public required string Url { get; init; }

    /// <summary>Local file path to write the downloaded content to.</summary>
    public required string Destination { get; init; }

    /// <summary>Optional request headers (name → value).</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// When <see langword="true"/>, overwrites an existing file at <see cref="Destination"/>.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool Overwrite { get; init; }

    /// <summary>Per-request timeout in seconds. Default: 300 (5 minutes).</summary>
    public int TimeoutSeconds { get; init; } = 300;
}
