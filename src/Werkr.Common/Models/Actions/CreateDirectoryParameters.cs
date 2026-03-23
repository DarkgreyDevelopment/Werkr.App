namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the CreateDirectory action.</summary>
public sealed record CreateDirectoryParameters {
    /// <summary>Full path of the directory to create.</summary>
    public required string Path { get; init; }
}
