namespace Werkr.Common.Models;

/// <summary>A named, persisted filter combination for a list page.</summary>
/// <remarks>
/// <see cref="Id"/> is <c>string</c> by design: local filters use client-generated
/// identifiers while server-synced filters are prefixed (<c>server-{dbId}</c>).
/// The shared <c>SavedFilterService</c> merges both sources by deduplicating on
/// this string key, so changing to <c>long</c> would break the dual-source architecture.
/// </remarks>
public sealed record SavedFilterView(
    string Id,
    string Name,
    bool IsDefault,
    bool IsShared,
    FilterCriteria Criteria
);
