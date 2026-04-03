using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Werkr.Common.Models;
using Werkr.Common.Models.Audit;
using Werkr.Core.Audit;
using Werkr.Core.Data;
using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Creates and queries immutable <see cref="TaskVersion"/> snapshots.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="auditService">Audit event service.</param>
/// <param name="logger">Logger instance.</param>
public sealed partial class TaskVersionService(
    WerkrDbContext dbContext,
    IAuditService auditService,
    ILogger<TaskVersionService> logger
) {

    /// <summary>
    /// Creates a new version snapshot for a task and updates its <see cref="WerkrTask.CurrentVersionId"/>.
    /// The task entity must already be tracked by the context (i.e., loaded via a tracked query).
    /// </summary>
    /// <param name="task">The tracked task entity to snapshot.</param>
    /// <param name="userId">The user who triggered the change, or null for system operations.</param>
    /// <param name="changeDescription">Optional human-readable description of the change.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="TaskVersion"/>.</returns>
    public async Task<TaskVersion> CreateVersionAsync(
        WerkrTask task,
        string? userId,
        string? changeDescription,
        CancellationToken ct = default
    ) {
        TaskDefinitionSnapshot snapshot = TaskDefinitionSnapshot.FromTask( task );

        const int MaxRetries = 3;
        TaskVersion? version = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++) {
            int maxVersion = await dbContext.TaskVersions
                .Where( v => v.TaskId == task.Id )
                .MaxAsync( v => (int?)v.VersionNumber, ct ) ?? 0;

            version = new( ) {
                TaskId = task.Id,
                VersionNumber = maxVersion + 1,
                Definition = snapshot.ToJson( ),
                CreatedByUserId = userId,
                ChangeDescription = changeDescription,
            };

            _ = dbContext.TaskVersions.Add( version );
            try {
                _ = await dbContext.SaveChangesAsync( ct );
                break;
            } catch (DbUpdateException ex) when (attempt < MaxRetries - 1 && DbExceptionHelper.IsUniqueConstraintViolation( ex )) {
                // Unique constraint violation — retry with fresh version number
                dbContext.ChangeTracker.Entries<TaskVersion>( )
                    .Where( e => e.Entity == version )
                    .ToList( )
                    .ForEach( e => e.State = EntityState.Detached );
                version = null;
                continue;
            }
        }

        if (version is null) {
            throw new InvalidOperationException( $"Failed to create version for task {task.Id} after {MaxRetries} retries." );
        }

        task.CurrentVersionId = version.Id;
        _ = await dbContext.SaveChangesAsync( ct );

        await auditService.LogAsync( new AuditEntry(
            EventTypeId: AuditEventType.TaskVersionCreated.ToEventId( ),
            ActorId: userId,
            ActorType: userId is not null ? "user" : "system",
            EntityType: "Task",
            EntityId: task.Id.ToString( ),
            ActionPerformed: $"Created version {version.VersionNumber}",
            Details: new {
                TaskId = task.Id,
                version.VersionNumber,
                ChangeDescription = changeDescription,
            }
        ), ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Created task version {VersionNumber} for task {TaskId} '{TaskName}'.",
                version.VersionNumber.ToString( ),
                task.Id.ToString( ),
                task.Name
            );
        }

        return version;
    }

    /// <summary>
    /// Retrieves a paginated list of versions for a task, newest first.
    /// </summary>
    /// <param name="taskId">The task ID.</param>
    /// <param name="limit">Maximum number of items per page.</param>
    /// <param name="offset">Number of items to skip.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paged result of task versions.</returns>
    public async Task<PagedResult<TaskVersion>> GetVersionsAsync(
        long taskId,
        int limit,
        int offset,
        CancellationToken ct = default
    ) {
        IQueryable<TaskVersion> query = dbContext.TaskVersions
            .AsNoTracking( )
            .Where( v => v.TaskId == taskId );

        int totalCount = await query.CountAsync( ct );

        List<TaskVersion> items = await query
            .OrderByDescending( v => v.VersionNumber )
            .Skip( offset )
            .Take( limit )
            .ToListAsync( ct );

        return new PagedResult<TaskVersion>( items, totalCount, limit, offset );
    }

    /// <summary>
    /// Retrieves a single version by ID, scoped to a task.
    /// </summary>
    /// <param name="taskId">The parent task ID.</param>
    /// <param name="versionId">The version ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task version, or null if not found or doesn't belong to the task.</returns>
    public async Task<TaskVersion?> GetVersionByIdAsync(
        long taskId,
        long versionId,
        CancellationToken ct = default
    ) =>
        await dbContext.TaskVersions
            .AsNoTracking( )
            .FirstOrDefaultAsync( v => v.Id == versionId && v.TaskId == taskId, ct );
}
