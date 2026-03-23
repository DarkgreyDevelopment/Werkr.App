namespace Werkr.Common.Models;

/// <summary>Request body for updating a configuration entry.</summary>
public sealed record ConfigurationUpdateRequest(
    string Value,
    int? ScopeLevel = null,
    string? ScopeId = null
);
