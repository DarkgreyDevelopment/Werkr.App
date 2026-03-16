namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the GetFileInfo action.</summary>
public sealed record GetFileInfoParameters {
    /// <summary>Path to the file or directory to inspect.</summary>
    public required string Path { get; init; }
}
