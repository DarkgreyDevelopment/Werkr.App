using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Werkr.Data;
using Werkr.Data.Entities.Tasks;

namespace Werkr.Core.Tasks;

/// <summary>
/// Provides CRUD operations for <see cref="WerkrTask"/> entities,
/// mediating between the API layer and the underlying <see cref="WerkrDbContext"/>.
/// </summary>
/// <param name="dbContext">Database context.</param>
/// <param name="logger">Logger instance.</param>
public sealed class TaskService(
    WerkrDbContext dbContext,
    ILogger<TaskService> logger
) {

    /// <summary>
    /// Creates a new task with randomized <see cref="WerkrTask.SyncIntervalMinutes"/>.
    /// </summary>
    /// <param name="task">The task entity to create. <c>Id</c> is generated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created task with its generated Id.</returns>
    /// <exception cref="ValidationException">Thrown when the task fails validation.</exception>
    public async Task<WerkrTask> CreateAsync(
        WerkrTask task,
        CancellationToken ct = default
    ) {
        Validate( task );

        // Randomize sync interval between 30–60 minutes
        task.SyncIntervalMinutes = RandomNumberGenerator.GetInt32(
            30,
            61
        );

        _ = dbContext.Tasks.Add( task );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation( "Created task {TaskId} '{TaskName}' (SyncInterval={Interval}m).",
                task.Id.ToString( ),
                task.Name,
                task.SyncIntervalMinutes.ToString( )
            );
        }

        return task;
    }

    /// <summary>
    /// Retrieves all tasks, optionally filtered by workflow.
    /// </summary>
    /// <param name="workflowId">If specified, only returns tasks belonging to this workflow.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of tasks.</returns>
    public async Task<IReadOnlyList<WerkrTask>> GetAllAsync(
        long? workflowId = null,
        CancellationToken ct = default
    ) {
        IQueryable<WerkrTask> query = dbContext.Tasks.AsNoTracking( );

        if (workflowId.HasValue) {
            query = query.Where( t => t.WorkflowId == workflowId.Value );
        }

        return await query.OrderBy( t => t.Name ).ToListAsync( ct );
    }

    /// <summary>
    /// Retrieves a single task by ID.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task, or null if not found.</returns>
    public async Task<WerkrTask?> GetByIdAsync(
        long id,
        CancellationToken ct = default
    ) =>
        await dbContext.Tasks.AsNoTracking( ).FirstOrDefaultAsync(
            t => t.Id == id,
            ct
        );

    /// <summary>
    /// Updates an existing task.
    /// </summary>
    /// <param name="task">The task entity with updated values. <c>Id</c> must match an existing task.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated task.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the task ID does not exist.</exception>
    /// <exception cref="ValidationException">Thrown when the task fails validation.</exception>
    public async Task<WerkrTask> UpdateAsync(
        WerkrTask task,
        CancellationToken ct = default
    ) {
        Validate( task );

        WerkrTask? existing = await dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == task.Id,
            ct
        )
            ?? throw new KeyNotFoundException( $"Task with Id={task.Id} was not found." );

        existing.Name = task.Name;
        existing.Description = task.Description;
        existing.ActionType = task.ActionType;
        existing.Content = task.Content;
        existing.Arguments = task.Arguments;
        existing.TargetTags = task.TargetTags;
        existing.Enabled = task.Enabled;
        existing.TimeoutMinutes = task.TimeoutMinutes;
        existing.SuccessCriteria = task.SuccessCriteria;
        existing.ScheduleId = task.ScheduleId;
        existing.WorkflowId = task.WorkflowId;
        existing.ActionSubType = task.ActionSubType;
        existing.ActionParameters = task.ActionParameters;

        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Updated task {TaskId} '{TaskName}'.",
                existing.Id.ToString( ),
                existing.Name
            );
        }

        return existing;
    }

    /// <summary>
    /// Deletes a task by ID.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when the task ID does not exist.</exception>
    public async Task DeleteAsync(
        long id,
        CancellationToken ct = default
    ) {
        WerkrTask? existing = await dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == id,
            ct
        )
            ?? throw new KeyNotFoundException( $"Task with Id={id} was not found." );

        _ = dbContext.Tasks.Remove( existing );
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Deleted task {TaskId} '{TaskName}'.",
                id.ToString( ),
                existing.Name
            );
        }
    }

    /// <summary>
    /// Toggles the <see cref="WerkrTask.Enabled"/> flag on a task.
    /// </summary>
    /// <param name="id">The task identifier.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when the task ID does not exist.</exception>
    public async Task SetEnabledAsync(
        long id,
        bool enabled,
        CancellationToken ct = default
    ) {
        WerkrTask? existing = await dbContext.Tasks.FirstOrDefaultAsync(
            t => t.Id == id,
            ct
        )
            ?? throw new KeyNotFoundException( $"Task with Id={id} was not found." );

        existing.Enabled = enabled;
        _ = await dbContext.SaveChangesAsync( ct );

        if (logger.IsEnabled( LogLevel.Information )) {
            logger.LogInformation(
                "Task {TaskId} enabled={Enabled}.",
                id.ToString( ),
                enabled.ToString( )
            );
        }
    }

    /// <summary>
    /// Validates a <see cref="WerkrTask"/> before create or update.
    /// </summary>
    /// <exception cref="ValidationException">Thrown when validation fails.</exception>
    private static void Validate( WerkrTask task ) {
        if (string.IsNullOrWhiteSpace( task.Name )) {
            throw new ValidationException( "Task name is required." );
        }

        // Action-type tasks use ActionSubType + ActionParameters instead of Content.
        // Content is only required for shell-type tasks (ShellCommand, PowerShell, etc.).
        if (task.ActionType != TaskActionType.Action && string.IsNullOrWhiteSpace( task.Content )) {
            throw new ValidationException( "Task content is required." );
        }

        if (!Enum.IsDefined( task.ActionType )) {
            throw new ValidationException( $"Invalid ActionType: {task.ActionType}." );
        }

        if (task.TargetTags.Length == 0) {
            throw new ValidationException( "At least one target tag is required." );
        }

        if (task.TimeoutMinutes.HasValue && task.TimeoutMinutes.Value <= 0) {
            throw new ValidationException( "TimeoutMinutes must be greater than zero." );
        }
    }
}
