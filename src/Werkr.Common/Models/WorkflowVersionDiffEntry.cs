namespace Werkr.Common.Models;

/// <summary>
/// Represents a single property-level difference between two workflow version snapshots.
/// </summary>
/// <param name="PropertyPath">The name of the changed property.</param>
/// <param name="OldValue">The previous value (null for <see cref="DiffChangeType.Added"/>).</param>
/// <param name="NewValue">The new value (null for <see cref="DiffChangeType.Removed"/>).</param>
/// <param name="ChangeType">The type of change.</param>
public sealed record WorkflowVersionDiffEntry(
    string PropertyPath,
    string? OldValue,
    string? NewValue,
    DiffChangeType ChangeType
);
