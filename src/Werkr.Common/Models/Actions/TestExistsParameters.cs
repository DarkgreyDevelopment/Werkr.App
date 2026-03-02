namespace Werkr.Common.Models.Actions;

/// <summary>Parameters for the TestExists action.</summary>
public sealed record TestExistsParameters {
    /// <summary>Path to test for existence.</summary>
    public required string Path { get; init; }

    /// <summary>What type of path to check: File, Directory, or Any.</summary>
    public PathType Type { get; init; } = PathType.Any;
}
