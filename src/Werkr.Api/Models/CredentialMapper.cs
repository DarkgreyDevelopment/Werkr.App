using Werkr.Common.Models;
using Werkr.Data.Entities.Configuration;

namespace Werkr.Api.Models;

/// <summary>
/// Maps between Credential entities and DTOs. Never maps decrypted value to DTO.
/// </summary>
internal static class CredentialMapper {

    /// <summary>Maps a <see cref="Credential"/> entity to a <see cref="CredentialDto"/>.</summary>
    public static CredentialDto ToDto( Credential entity ) =>
        new(
            Id: entity.Id,
            Name: entity.Name,
            Type: entity.Type.ToString( ),
            Description: entity.Description,
            CreatedUtc: entity.CreatedUtc,
            ModifiedUtc: entity.ModifiedUtc,
            CreatedByUserId: entity.CreatedByUserId,
            ModifiedByUserId: entity.ModifiedByUserId,
            AgentScopeIds: [.. entity.AgentScopes.Select( s => s.AgentConnectionId )]
        );
}
