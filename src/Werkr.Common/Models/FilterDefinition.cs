namespace Werkr.Common.Models;

/// <summary>Declares the available filter fields for a list page.</summary>
public sealed record FilterDefinition(
    IReadOnlyList<FilterField> Fields
);
