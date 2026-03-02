namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the WriteContent action.</summary>
public sealed record WriteContentParameters {
    /// <summary>Full path of the file to write to.</summary>
    public required string Path { get; init; }

    /// <summary>Content to write.</summary>
    public required string Content { get; init; }

    /// <summary>Whether to append to the file instead of overwriting.</summary>
    public bool Append { get; init; }

    /// <summary>Text encoding (e.g. "utf-8", "ascii").</summary>
    public string Encoding { get; init; } = "utf-8";
}
