namespace Werkr.Core.Retention;

/// <summary>
/// Result of a retention sweep for a single entity type.
/// </summary>
/// <param name="EntityType">The entity type that was swept.</param>
/// <param name="DeletedCount">Number of records deleted during this sweep.</param>
/// <param name="OldestDeleted">Timestamp of the oldest deleted record, or null if none were deleted.</param>
/// <param name="NewestDeleted">Timestamp of the newest deleted record, or null if none were deleted.</param>
public sealed record RetentionSweepResult(
    string EntityType,
    int DeletedCount,
    DateTime? OldestDeleted,
    DateTime? NewestDeleted
);
