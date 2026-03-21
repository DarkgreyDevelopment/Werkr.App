namespace Werkr.Core.Retention;

/// <summary>
/// Preview of records eligible for retention-based deletion.
/// </summary>
/// <param name="EntityType">The entity type being previewed.</param>
/// <param name="EligibleCount">Number of records that would be deleted.</param>
/// <param name="OldestTimestamp">Timestamp of the oldest eligible record, or null if none.</param>
/// <param name="NewestTimestamp">Timestamp of the newest eligible record, or null if none.</param>
public sealed record RetentionPreview(
    string EntityType,
    int EligibleCount,
    DateTime? OldestTimestamp,
    DateTime? NewestTimestamp
);
