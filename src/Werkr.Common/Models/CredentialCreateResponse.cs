namespace Werkr.Common.Models;

/// <summary>
/// Response after credential creation. Includes the plaintext value ONE TIME only.
/// Subsequent reads will return <see cref="CredentialDto"/> with a masked value.
/// </summary>
public sealed record CredentialCreateResponse(
    long Id,
    string Name,
    string Type,
    string? Description,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    string CreatedByUserId,
    string ModifiedByUserId,
    IReadOnlyList<Guid> AgentScopeIds,
    string PlaintextValue
);
