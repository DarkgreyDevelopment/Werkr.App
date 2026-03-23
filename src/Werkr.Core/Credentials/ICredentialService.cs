using Werkr.Common.Models;

namespace Werkr.Core.Credentials;

/// <summary>
/// Service for managing encrypted credentials with agent scoping.
/// </summary>
public interface ICredentialService {

    /// <summary>Creates a new credential. Returns plaintext value ONE TIME.</summary>
    Task<CredentialCreateResponse> CreateAsync( CredentialCreateRequest request, string userId, CancellationToken ct );

    /// <summary>Lists all credentials with masked values.</summary>
    Task<IReadOnlyList<CredentialDto>> GetAllAsync( CancellationToken ct );

    /// <summary>Gets a single credential by ID with masked value.</summary>
    Task<CredentialDto?> GetByIdAsync( long id, CancellationToken ct );

    /// <summary>Updates credential value and/or metadata.</summary>
    Task<CredentialDto> UpdateAsync( long id, CredentialUpdateRequest request, string userId, CancellationToken ct );

    /// <summary>
    /// Renames a credential and atomically updates all task ActionParameters
    /// that reference the old name, creating new TaskVersions for each.
    /// </summary>
    Task<CredentialDto> RenameAsync( long id, string newName, string userId, CancellationToken ct );

    /// <summary>
    /// Deletes a credential. Returns 409-style error if tasks reference it.
    /// </summary>
    Task DeleteAsync( long id, string userId, CancellationToken ct );

    /// <summary>Replaces agent scope entries for a credential.</summary>
    Task UpdateScopesAsync( long id, IReadOnlyList<Guid> agentConnectionIds, string userId, CancellationToken ct );

    /// <summary>
    /// Resolves a credential value for a specific agent.
    /// Returns a result distinguishing not-found from out-of-scope.
    /// Logs an audit event for each successful access.
    /// </summary>
    Task<CredentialResolveResult> ResolveForAgentAsync( string credentialName, Guid agentConnectionId, string userId, CancellationToken ct );
}
