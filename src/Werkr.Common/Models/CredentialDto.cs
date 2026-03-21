namespace Werkr.Common.Models;

/// <summary>
/// Response DTO for a credential. Never includes the decrypted value.
/// </summary>
public sealed record CredentialDto(
    long Id,
    string Name,
    string Type,
    string? Description,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    string CreatedByUserId,
    string ModifiedByUserId,
    IReadOnlyList<Guid> AgentScopeIds
);
