using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Tasks;
using Werkr.Data;
using Werkr.Data.Entities.Configuration;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Credentials;

/// <summary>
/// CRUD, rename cascade, scope management, and injection resolution for credentials.
/// Credential values are transparently encrypted/decrypted by EF Core's
/// <c>EncryptedStringConverter</c> on the <see cref="Credential.EncryptedValue"/> column.
/// </summary>
public sealed partial class CredentialService(
    WerkrDbContext dbContext,
    TaskVersionService taskVersionService,
    IAuditService auditService,
    ILogger<CredentialService> logger
) : ICredentialService {

    private const string MaskedValue = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    /// <inheritdoc/>
    public async Task<CredentialCreateResponse> CreateAsync(
        CredentialCreateRequest request, string userId, CancellationToken ct
    ) {
        if (!Enum.TryParse<CredentialType>( request.Type, ignoreCase: true, out CredentialType credType )) {
            throw new ArgumentException( $"Unknown credential type '{request.Type}'." );
        }

        DateTime now = DateTime.UtcNow;
        Credential entity = new( ) {
            Name = request.Name,
            Type = credType,
            EncryptedValue = request.Value, // EF encryption handles storage
            Description = request.Description,
            CreatedUtc = now,
            ModifiedUtc = now,
            CreatedByUserId = userId,
            ModifiedByUserId = userId,
        };

        _ = dbContext.Credentials.Add( entity );

        // Agent scopes
        if (request.AgentScopeIds is { Count: > 0 }) {
            foreach (Guid agentId in request.AgentScopeIds) {
                _ = dbContext.CredentialAgentScopes.Add( new CredentialAgentScope {
                    Credential = entity,
                    AgentConnectionId = agentId,
                } );
            }
        }

        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.CredentialCreated.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "Credential",
            EntityId: entity.Id.ToString( ),
            ActionPerformed: "Created",
            Details: new { entity.Name, Type = entity.Type.ToString( ) }
        ), ct );

        LogCredentialCreated( logger, entity.Name, entity.Id );

        return new CredentialCreateResponse(
            Id: entity.Id,
            Name: entity.Name,
            Type: entity.Type.ToString( ),
            Description: entity.Description,
            CreatedUtc: entity.CreatedUtc,
            ModifiedUtc: entity.ModifiedUtc,
            CreatedByUserId: entity.CreatedByUserId,
            ModifiedByUserId: entity.ModifiedByUserId,
            AgentScopeIds: request.AgentScopeIds ?? [],
            PlaintextValue: request.Value
        );
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CredentialDto>> GetAllAsync( CancellationToken ct ) {
        List<Credential> credentials = await dbContext.Credentials
            .AsNoTracking( )
            .Include( c => c.AgentScopes )
            .OrderBy( c => c.Name )
            .ToListAsync( ct );

        return [.. credentials.Select( ToDto )];
    }

    /// <inheritdoc/>
    public async Task<CredentialDto?> GetByIdAsync( long id, CancellationToken ct ) {
        Credential? credential = await dbContext.Credentials
            .AsNoTracking( )
            .Include( c => c.AgentScopes )
            .FirstOrDefaultAsync( c => c.Id == id, ct );

        return credential is null ? null : ToDto( credential );
    }

    /// <inheritdoc/>
    public async Task<CredentialDto> UpdateAsync(
        long id, CredentialUpdateRequest request, string userId, CancellationToken ct
    ) {
        Credential entity = await dbContext.Credentials
            .Include( c => c.AgentScopes )
            .FirstOrDefaultAsync( c => c.Id == id, ct )
            ?? throw new KeyNotFoundException( $"Credential {id} not found." );

        if (!string.IsNullOrEmpty( request.Value )) {
            entity.EncryptedValue = request.Value;
        }
        if (request.Description is not null) {
            entity.Description = request.Description;
        }
        if (!string.IsNullOrEmpty( request.Type ) &&
            Enum.TryParse<CredentialType>( request.Type, ignoreCase: true, out CredentialType newType )) {
            entity.Type = newType;
        }

        entity.ModifiedUtc = DateTime.UtcNow;
        entity.ModifiedByUserId = userId;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.CredentialUpdated.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "Credential",
            EntityId: entity.Id.ToString( ),
            ActionPerformed: "Updated",
            Details: new { entity.Name }
        ), ct );

        return ToDto( entity );
    }

    /// <inheritdoc/>
    public async Task<CredentialDto> RenameAsync(
        long id, string newName, string userId, CancellationToken ct
    ) {
        Credential entity = await dbContext.Credentials
            .Include( c => c.AgentScopes )
            .FirstOrDefaultAsync( c => c.Id == id, ct )
            ?? throw new KeyNotFoundException( $"Credential {id} not found." );

        string oldName = entity.Name;

        await using IDbContextTransaction tx = await dbContext.Database.BeginTransactionAsync( ct );
        try {
            entity.Name = newName;
            entity.ModifiedUtc = DateTime.UtcNow;
            entity.ModifiedByUserId = userId;

            // Cascade: update all task ActionParameters referencing the old name
            List<WerkrTask> tasks = await dbContext.Tasks
                .Where( t => t.ActionParameters != null && t.ActionParameters.Contains( oldName ) )
                .ToListAsync( ct );

            int cascadeCount = 0;
            foreach (WerkrTask task in tasks) {
                string? updated = CredentialResolver.ReplaceCredentialName( task.ActionParameters, oldName, newName );
                if (updated is not null) {
                    task.ActionParameters = updated;
                    _ = await taskVersionService.CreateVersionAsync(
                        task, userId, $"Credential renamed: {oldName} → {newName}", ct );
                    cascadeCount++;
                }
            }

            _ = await dbContext.SaveChangesAsync( ct );
            await tx.CommitAsync( ct );

            await auditService.LogAsync( new AuditEntry(
                EventTypeId: AuditEventType.CredentialRenamed.ToEventId( ),
                ActorId: userId,
                ActorType: "User",
                EntityType: "Credential",
                EntityId: entity.Id.ToString( ),
                ActionPerformed: "Renamed",
                Details: new { OldName = oldName, NewName = newName, CascadedTasks = cascadeCount }
            ), ct );

            LogCredentialRenamed( logger, oldName, newName, cascadeCount );

            return ToDto( entity );
        } catch {
            await tx.RollbackAsync( ct );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync( long id, string userId, CancellationToken ct ) {
        Credential entity = await dbContext.Credentials
            .FirstOrDefaultAsync( c => c.Id == id, ct )
            ?? throw new KeyNotFoundException( $"Credential {id} not found." );

        // Check for references in task ActionParameters (SQL pre-filter + JSON-aware post-filter)
        List<WerkrTask> candidateTasks = await dbContext.Tasks
            .Where( t => t.ActionParameters != null && t.ActionParameters.Contains( entity.Name ) )
            .ToListAsync( ct );

        List<string> referencingTasks = [.. candidateTasks
            .Where( t => CredentialResolver.FindCredentialReferences( t.ActionParameters )
                .Any( name => string.Equals( name, entity.Name, StringComparison.OrdinalIgnoreCase ) ) )
            .Select( t => t.Name )];

        if (referencingTasks.Count > 0) {
            throw new InvalidOperationException(
                $"Cannot delete credential '{entity.Name}': referenced by tasks: {string.Join( ", ", referencingTasks )}" );
        }

        _ = dbContext.Credentials.Remove( entity );
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.CredentialDeleted.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "Credential",
            EntityId: entity.Id.ToString( ),
            ActionPerformed: "Deleted",
            Details: new { entity.Name }
        ), ct );
    }

    /// <inheritdoc/>
    public async Task UpdateScopesAsync(
        long id, IReadOnlyList<Guid> agentConnectionIds, string userId, CancellationToken ct
    ) {
        Credential entity = await dbContext.Credentials
            .Include( c => c.AgentScopes )
            .FirstOrDefaultAsync( c => c.Id == id, ct )
            ?? throw new KeyNotFoundException( $"Credential {id} not found." );

        // Replace scopes
        dbContext.CredentialAgentScopes.RemoveRange( entity.AgentScopes );
        foreach (Guid agentId in agentConnectionIds) {
            _ = dbContext.CredentialAgentScopes.Add( new CredentialAgentScope {
                CredentialId = id,
                AgentConnectionId = agentId,
            } );
        }

        entity.ModifiedUtc = DateTime.UtcNow;
        entity.ModifiedByUserId = userId;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.CredentialScopeUpdated.ToEventId( ),
            ActorId: userId,
            ActorType: "User",
            EntityType: "Credential",
            EntityId: entity.Id.ToString( ),
            ActionPerformed: "ScopeUpdated",
            Details: new { entity.Name, AgentCount = agentConnectionIds.Count }
        ), ct );
    }

    /// <inheritdoc/>
    public async Task<CredentialResolveResult> ResolveForAgentAsync(
        string credentialName, Guid agentConnectionId, string userId, CancellationToken ct
    ) {
        Credential? entity = await dbContext.Credentials
            .Include( c => c.AgentScopes )
            .FirstOrDefaultAsync( c => c.Name == credentialName, ct );

        if (entity is null) {
            return new CredentialResolveResult( Found: false, InScope: false, DecryptedValue: null );
        }

        // Scope check: no scopes = available to all; scopes = must be listed
        if (entity.AgentScopes.Count > 0 &&
            !entity.AgentScopes.Any( s => s.AgentConnectionId == agentConnectionId )) {
            LogScopeRejected( logger, credentialName, agentConnectionId );
            return new CredentialResolveResult( Found: true, InScope: false, DecryptedValue: null );
        }

        // Audit access (credential name only, NOT value)
        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.CredentialAccessed.ToEventId( ),
            ActorId: userId,
            ActorType: "System",
            EntityType: "Credential",
            EntityId: entity.Id.ToString( ),
            ActionPerformed: "Accessed",
            Details: new { entity.Name, AgentConnectionId = agentConnectionId.ToString( ) }
        ), ct );

        // EF transparently decrypts EncryptedValue
        return new CredentialResolveResult( Found: true, InScope: true, DecryptedValue: entity.EncryptedValue );
    }

    private static CredentialDto ToDto( Credential entity ) =>
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

    [LoggerMessage( Level = LogLevel.Information, Message = "Credential created: {Name} (id={Id})" )]
    private static partial void LogCredentialCreated( ILogger logger, string name, long id );

    [LoggerMessage( Level = LogLevel.Information, Message = "Credential renamed: {OldName} → {NewName} ({CascadeCount} tasks updated)" )]
    private static partial void LogCredentialRenamed( ILogger logger, string oldName, string newName, int cascadeCount );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Credential scope rejected: {CredentialName} not available to agent {AgentId}" )]
    private static partial void LogScopeRejected( ILogger logger, string credentialName, Guid agentId );
}
