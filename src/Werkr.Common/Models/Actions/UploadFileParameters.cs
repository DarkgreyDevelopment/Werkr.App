namespace Werkr.Common.Models.Actions;

/// <summary>
/// Parameters for the <c>UploadFile</c> action. Uploads a local file to a URL
/// using multipart/form-data.
/// </summary>
public sealed record UploadFileParameters {

    /// <summary>The local file path to upload. Resolved via <c>IFilePathResolver</c>.</summary>
    public required string FilePath { get; init; }

    /// <summary>The URL to upload the file to.</summary>
    public required string Url { get; init; }

    /// <summary>HTTP method. Defaults to <c>POST</c>.</summary>
    public string Method { get; init; } = "POST";

    /// <summary>The form field name for the file part. Defaults to <c>file</c>.</summary>
    public string FormFieldName { get; init; } = "file";

    /// <summary>Optional additional request headers.</summary>
    public Dictionary<string, string>? Headers { get; init; }

    /// <summary>Request timeout in seconds. Defaults to 300 (5 minutes).</summary>
    public int TimeoutSeconds { get; init; } = 300;
}
