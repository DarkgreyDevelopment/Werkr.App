namespace Werkr.Common.Models;

/// <summary>Request body for updating credential agent scopes.</summary>
public sealed record CredentialScopeUpdateRequest(
    IReadOnlyList<Guid> AgentConnectionIds
);
