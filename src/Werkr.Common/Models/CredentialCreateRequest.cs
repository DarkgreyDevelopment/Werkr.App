namespace Werkr.Common.Models;

/// <summary>Request body for creating a credential.</summary>
public sealed record CredentialCreateRequest(
    string Name,
    string Type,
    string Value,
    string? Description = null,
    IReadOnlyList<Guid>? AgentScopeIds = null
);
