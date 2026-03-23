namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the ClearContent action.</summary>
public sealed record ClearContentParameters {
    /// <summary>Full path of the file to truncate.</summary>
    public required string Path { get; init; }
}
