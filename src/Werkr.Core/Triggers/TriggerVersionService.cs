using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Data;
using Werkr.Data;
using Werkr.Data.Entities.Triggers;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Triggers;

/// <summary>
/// Creates and queries immutable <see cref="TriggerVersion"/> snapshots.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="auditService">Audit event service.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class TriggerVersionService(
    WerkrDbContext dbContext,
    IAuditService auditService,
    ILogger<TriggerVersionService> logger
) {

    /// <summary>
    /// Creates a new version snapshot for a trigger and updates its CurrentVersionId.
    /// </summary>
    public async Task<TriggerVersion> CreateVersionAsync(
        FileMonitorTrigger trigger,
        string? userId,
        string? changeDescription,
        CancellationToken ct = default
    ) {
        TriggerDefinitionSnapshot snapshot = TriggerDefinitionSnapshot.FromTrigger( trigger );

        const int MaxRetries = 3;
        TriggerVersion? version = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++) {
            int maxVersion = await dbContext.TriggerVersions
                .Where( v => v.TriggerId == trigger.Id )
                .MaxAsync( v => (int?)v.VersionNumber, ct ) ?? 0;

            version = new( ) {
                TriggerId = trigger.Id,
                VersionNumber = maxVersion + 1,
                Definition = snapshot.ToJson( ),
                CreatedByUserId = userId,
                ChangeDescription = changeDescription,
            };

            _ = dbContext.TriggerVersions.Add( version );
            try {
                _ = await dbContext.SaveChangesAsync( ct );
                break;
            } catch (DbUpdateException ex) when (attempt < MaxRetries - 1 && DbExceptionHelper.IsUniqueConstraintViolation( ex )) {
                dbContext.ChangeTracker.Entries<TriggerVersion>( )
                    .Where( e => e.Entity == version )
                    .ToList( )
                    .ForEach( e => e.State = EntityState.Detached );
                version = null;
                continue;
            }
        }

        if (version is null) {
            throw new InvalidOperationException( $"Failed to create version for trigger {trigger.Id} after {MaxRetries} retries." );
        }

        trigger.CurrentVersionId = version.Id;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.TriggerVersionCreated.ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "user" : "system",
            EntityType: "Trigger",
            EntityId: trigger.Id.ToString( ),
            ActionPerformed: $"Created version {version.VersionNumber}",
            Details: new {
                TriggerId = trigger.Id,
                version.VersionNumber,
                ChangeDescription = changeDescription,
            }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created trigger version {VersionNumber} for trigger {TriggerId}.",
                version.VersionNumber.ToString( ),
                trigger.Id.ToString( )
            );
        }

        return version;
    }

    /// <summary>Retrieves a paginated list of versions for a trigger, newest first.</summary>
    public async Task<PagedResult<TriggerVersion>> GetVersionsAsync(
        long triggerId,
        int limit,
        int offset,
        CancellationToken ct = default
    ) {
        IQueryable<TriggerVersion> query = dbContext.TriggerVersions
            .AsNoTracking( )
            .Where( v => v.TriggerId == triggerId );

        int totalCount = await query.CountAsync( ct );

        List<TriggerVersion> items = await query
            .OrderByDescending( v => v.VersionNumber )
            .Skip( offset )
            .Take( limit )
            .ToListAsync( ct );

        return new PagedResult<TriggerVersion>( items, totalCount, limit, offset );
    }

    /// <summary>Retrieves a single version by ID, scoped to a trigger.</summary>
    public async Task<TriggerVersion?> GetVersionByIdAsync(
        long triggerId,
        long versionId,
        CancellationToken ct = default
    ) =>
        await dbContext.TriggerVersions
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == versionId && v.TriggerId == triggerId, ct );

    /// <summary>
    /// Resolves the effective workflow version ID for execution based on the trigger's binding mode.
    /// </summary>
    /// <param name="trigger">The trigger entity with its binding mode.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow version ID to use, or null if no version is available.</returns>
    public async Task<long?> ResolveWorkflowVersionIdAsync(
        FileMonitorTrigger trigger,
        CancellationToken ct = default
    ) {
        if (trigger.VersionBindingMode == VersionBindingMode.Pinned) {
            return trigger.PinnedWorkflowVersionId;
        }

        // Latest mode — resolve from workflow's current version
        Workflow? workflow = await dbContext.Workflows
            .AsNoTracking( )
            .FirstOrDefaultAsync( w => w.Id == trigger.WorkflowId, ct );

        return workflow?.CurrentVersionId;
    }
}
