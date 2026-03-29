using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Data;
using Werkr.Data;
using Werkr.Data.Entities.Workflows;

namespace Werkr.Core.Workflows;

/// <summary>
/// Creates and queries immutable <see cref="WorkflowVersion"/> snapshots.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="auditService">Audit event service.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class WorkflowVersionService(
    WerkrDbContext dbContext,
    IAuditService auditService,
    ILogger<WorkflowVersionService> logger
) {

    /// <summary>
    /// Creates a new version snapshot for a workflow and updates its <see cref="Workflow.CurrentVersionId"/>.
    /// The workflow entity must already be tracked by the context with Steps (including Dependencies) and Variables loaded.
    /// </summary>
    /// <param name="workflow">The tracked workflow entity to snapshot.</param>
    /// <param name="userId">The user who triggered the change, or null for system operations.</param>
    /// <param name="changeDescription">Optional human-readable description of the change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="WorkflowVersion"/>.</returns>
    public async Task<WorkflowVersion> CreateVersionAsync(
        Workflow workflow,
        string? userId,
        string? changeDescription,
        CancellationToken ct = default
    ) {
        WorkflowDefinitionSnapshot snapshot = WorkflowDefinitionSnapshot.FromWorkflow( workflow );

        const int MaxRetries = 3;
        WorkflowVersion? version = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++) {
            int maxVersion = await dbContext.Set<WorkflowVersion>( )
                .Where( v => v.WorkflowId == workflow.Id )
                .MaxAsync( v => (int?)v.VersionNumber, ct ) ?? 0;

            version = new( ) {
                WorkflowId = workflow.Id,
                VersionNumber = maxVersion + 1,
                Definition = snapshot.ToJson( ),
                CreatedByUserId = userId,
                ChangeDescription = changeDescription,
            };

            _ = dbContext.Set<WorkflowVersion>( ).Add( version );
            try {
                _ = await dbContext.SaveChangesAsync( ct );
                break;
            } catch (DbUpdateException ex) when (attempt < MaxRetries - 1 && DbExceptionHelper.IsUniqueConstraintViolation( ex )) {
                dbContext.ChangeTracker.Entries<WorkflowVersion>( )
                    .Where( e => e.Entity == version )
                    .ToList( )
                    .ForEach( e => e.State = EntityState.Detached );
                version = null;
                continue;
            }
        }

        if (version is null) {
            throw new InvalidOperationException( $"Failed to create version for workflow {workflow.Id} after {MaxRetries} retries." );
        }

        workflow.CurrentVersionId = version.Id;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.WorkflowVersionCreated.ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "user" : "system",
            EntityType: "Workflow",
            EntityId: workflow.Id.ToString( ),
            ActionPerformed: $"Created version {version.VersionNumber}",
            Details: new {
                WorkflowId = workflow.Id,
                version.VersionNumber,
                ChangeDescription = changeDescription,
            }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created workflow version {VersionNumber} for workflow {WorkflowId} '{WorkflowName}'.",
                version.VersionNumber.ToString( ),
                workflow.Id.ToString( ),
                workflow.Name
            );
        }

        return version;
    }

    /// <summary>
    /// Retrieves a paginated list of versions for a workflow, newest first.
    /// </summary>
    public async Task<PagedResult<WorkflowVersion>> GetVersionsAsync(
        long workflowId,
        int limit,
        int offset,
        CancellationToken ct = default
    ) {
        IQueryable<WorkflowVersion> query = dbContext.Set<WorkflowVersion>( )
            .AsNoTracking( )
            .Where( v => v.WorkflowId == workflowId );

        int totalCount = await query.CountAsync( ct );

        List<WorkflowVersion> items = await query
            .OrderByDescending( v => v.VersionNumber )
            .Skip( offset )
            .Take( limit )
            .ToListAsync( ct );

        return new PagedResult<WorkflowVersion>( items, totalCount, limit, offset );
    }

    /// <summary>
    /// Retrieves a single version by ID, scoped to a workflow.
    /// </summary>
    public async Task<WorkflowVersion?> GetVersionByIdAsync(
        long workflowId,
        long versionId,
        CancellationToken ct = default
    ) =>
        await dbContext.Set<WorkflowVersion>( )
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == versionId && v.WorkflowId == workflowId, ct );

    /// <summary>
    /// Rolls back a workflow to a previous version by creating a NEW version with the target version's definition content.
    /// </summary>
    /// <param name="workflowId">The workflow ID.</param>
    /// <param name="targetVersionId">The version ID to roll back to.</param>
    /// <param name="userId">The user performing the rollback.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The newly created version, or null if the target version was not found.</returns>
    public async Task<WorkflowVersion?> RollbackAsync(
        long workflowId,
        long targetVersionId,
        string? userId,
        CancellationToken ct = default
    ) {
        WorkflowVersion? targetVersion = await dbContext.Set<WorkflowVersion>( )
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == targetVersionId && v.WorkflowId == workflowId, ct );

        if (targetVersion is null) {
            return null;
        }

        // Load the workflow with its related data for snapshot
        Workflow? workflow = await dbContext.Workflows
            .Include( w => w.Steps )
                .ThenInclude( s => s.Dependencies )
            .Include( w => w.Variables )
            .FirstOrDefaultAsync( w => w.Id == workflowId, ct );

        if (workflow is null) {
            return null;
        }

        // Create new version using the target's definition
        int maxVersion = await dbContext.Set<WorkflowVersion>( )
            .Where( v => v.WorkflowId == workflowId )
            .MaxAsync( v => (int?)v.VersionNumber, ct ) ?? 0;

        WorkflowVersion rollbackVersion = new( ) {
            WorkflowId = workflowId,
            VersionNumber = maxVersion + 1,
            Definition = targetVersion.Definition,
            CreatedByUserId = userId,
            ChangeDescription = $"Rollback to version {targetVersion.VersionNumber}",
        };

        _ = dbContext.Set<WorkflowVersion>( ).Add( rollbackVersion );
        _ = await dbContext.SaveChangesAsync( ct );

        workflow.CurrentVersionId = rollbackVersion.Id;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.WorkflowVersionRollback.ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "user" : "system",
            EntityType: "Workflow",
            EntityId: workflowId.ToString( ),
            ActionPerformed: $"Rolled back to version {targetVersion.VersionNumber}, created version {rollbackVersion.VersionNumber}",
            Details: new {
                WorkflowId = workflowId,
                TargetVersionNumber = targetVersion.VersionNumber,
                NewVersionNumber = rollbackVersion.VersionNumber,
            }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Rolled back workflow {WorkflowId} to version {TargetVersion}, created version {NewVersion}.",
                workflowId.ToString( ),
                targetVersion.VersionNumber.ToString( ),
                rollbackVersion.VersionNumber.ToString( )
            );
        }

        return rollbackVersion;
    }
}
