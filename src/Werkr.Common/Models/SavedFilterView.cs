namespace Werkr.Common.Models;

/// <summary>A named, persisted filter combination for a list page.</summary>
public sealed record SavedFilterView(
    string Id,
    string Name,
    bool IsDefault,
    bool IsShared,
    FilterCriteria Criteria
);
