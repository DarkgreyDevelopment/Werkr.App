namespace Werkr.Common.Models;

/// <summary>
/// Represents a single property-level difference between two task version snapshots.
/// </summary>
/// <param name="PropertyPath">The name of the changed property.</param>
/// <param name="OldValue">The previous value (null for <see cref="DiffChangeType.Added"/>).</param>
/// <param name="NewValue">The new value (null for <see cref="DiffChangeType.Removed"/>).</param>
/// <param name="ChangeType">The type of change.</param>
public sealed record TaskVersionDiffEntry(
    string PropertyPath,
    string? OldValue,
    string? NewValue,
    DiffChangeType ChangeType
);

/// <summary>
/// Classifies the type of change between two version snapshots.
/// </summary>
public enum DiffChangeType {
    /// <summary>A value was added where none existed before.</summary>
    Added,

    /// <summary>An existing value was removed.</summary>
    Removed,

    /// <summary>An existing value was changed.</summary>
    Modified,
}
