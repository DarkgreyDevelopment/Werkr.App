namespace Werkr.Common.Configuration;

/// <summary>
/// Configuration for file-based job output storage.
/// </summary>
public sealed class JobOutputOptions {
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "JobOutput";

    /// <summary>
    /// Directory where job output log files are stored.
    /// Defaults to <c>job-output</c> relative to the content root.
    /// </summary>
    public string OutputDirectory { get; set; } = "job-output";

    /// <summary>
    /// Maximum number of characters to store in the database as a tail preview.
    /// </summary>
    public int TailPreviewLength { get; set; } = 2000;
}
